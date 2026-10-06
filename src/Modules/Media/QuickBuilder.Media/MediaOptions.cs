using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;

namespace QuickBuilder.Media;

/// <summary>
/// Operator settings for the media library (003 research, "Configuration keys introduced").
/// </summary>
internal sealed class MediaOptions
{
    public const string SectionName = "Media";

    internal const long MinimumFileSize = 1_024;
    internal const long MaximumFileSize = 1_073_741_824;

    internal static readonly TimeSpan MinimumGracePeriod = TimeSpan.FromHours(1);
    internal static readonly TimeSpan MaximumGracePeriod = TimeSpan.FromDays(30);

    /// <summary>Largest accepted upload, in bytes. 20 MiB by default (FR-008).</summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// The types uploads may have. Empty means all six built-in types (filled in by
    /// post-configuration), so that setting <c>Media__AllowedTypes__0</c> alone narrows the
    /// list instead of overriding one entry of a six-entry default.
    /// </summary>
    public string[] AllowedTypes { get; set; } = [];

    /// <summary>How old stored bytes without a library entry must be before the sweep removes them (FR-012).</summary>
    public TimeSpan OrphanGracePeriod { get; set; } = TimeSpan.FromDays(1);

    internal const long MinimumImagePixels = 1_000_000;
    internal const long MaximumImagePixels = 1_000_000_000;

    /// <summary>
    /// Images with more pixels are refused at upload, and never decoded for variants
    /// (013 FR-027, research MV-10). 100 megapixels by default.
    /// </summary>
    public long MaxImagePixels { get; set; } = 100_000_000;

    /// <summary>Resized copies of images (013 research MV-11, MV-12).</summary>
    public VariantOptions Variants { get; set; } = new();

    /// <summary>The allowed types as <see cref="MediaFileType"/>s. Valid only after validation.</summary>
    internal IReadOnlyList<MediaFileType> AllowedFileTypes =>
        [.. AllowedTypes.Select(MediaFileType.FromMime).OfType<MediaFileType>().Distinct()];
}

/// <summary>Operator settings for image variants (013 research MV-11, MV-12).</summary>
internal sealed class VariantOptions
{
    /// <summary>The default widths, in pixels. The smallest is the thumbnail width (MV-13).</summary>
    internal static readonly int[] DefaultWidths = [320, 640, 960, 1280, 1920, 2560];

    internal const int MinimumWidth = 64;
    internal const int MaximumWidth = 4096;
    internal const int MaximumWidthCount = 10;
    internal const int MaximumConcurrency = 64;

    internal static readonly TimeSpan MinimumTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The widths offered, ascending. Empty means <see cref="DefaultWidths"/> (filled in by
    /// post-configuration), so that setting <c>Media__Variants__Widths__0</c> alone replaces the
    /// list rather than one entry of it.
    /// </summary>
    public int[] Widths { get; set; } = [];

    /// <summary>How many variants one instance produces at the same time (FR-029).</summary>
    public int MaxConcurrent { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>How long producing one variant may take, waiting included, before the original is used (FR-029).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The thumbnail width: the smallest configured width (013 clarification).</summary>
    internal int ThumbnailWidth => Widths.Min();

    /// <summary>The widths as a set, for address parsing.</summary>
    internal IReadOnlySet<int> WidthSet => Widths.ToHashSet();
}

/// <summary>Fills in defaults that configuration binding cannot express.</summary>
internal sealed class MediaOptionsDefaults : IPostConfigureOptions<MediaOptions>
{
    public void PostConfigure(string? name, MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.AllowedTypes.Length == 0)
        {
            options.AllowedTypes = [.. MediaFileType.All.Select(t => t.Mime)];
        }

        options.Variants ??= new VariantOptions();
        if (options.Variants.Widths.Length == 0)
        {
            options.Variants.Widths = [.. VariantOptions.DefaultWidths];
        }
    }
}

/// <summary>Startup validation for <see cref="MediaOptions"/>, naming the offending key.</summary>
internal sealed class MediaOptionsValidator : IValidateOptions<MediaOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.MaxFileSizeBytes is < MediaOptions.MinimumFileSize or > MediaOptions.MaximumFileSize)
        {
            failures.Add(
                $"Media:MaxFileSizeBytes must be between {MediaOptions.MinimumFileSize} and " +
                $"{MediaOptions.MaximumFileSize}; it is {options.MaxFileSizeBytes}.");
        }

        if (options.AllowedTypes.Length == 0)
        {
            failures.Add("Media:AllowedTypes must name at least one type.");
        }

        foreach (var type in options.AllowedTypes)
        {
            if (MediaFileType.FromMime(type) is null)
            {
                failures.Add(
                    $"Media:AllowedTypes contains '{type}', which is not an allowed type. It may only narrow " +
                    $"the built-in list: {string.Join(", ", MediaFileType.All.Select(t => t.Mime))}.");
            }
        }

        if (options.OrphanGracePeriod < MediaOptions.MinimumGracePeriod
            || options.OrphanGracePeriod > MediaOptions.MaximumGracePeriod)
        {
            failures.Add(
                $"Media:OrphanGracePeriod must be between {MediaOptions.MinimumGracePeriod} and " +
                $"{MediaOptions.MaximumGracePeriod}; it is {options.OrphanGracePeriod}.");
        }

        if (options.MaxImagePixels is < MediaOptions.MinimumImagePixels or > MediaOptions.MaximumImagePixels)
        {
            failures.Add(
                $"Media:MaxImagePixels must be between {MediaOptions.MinimumImagePixels} and " +
                $"{MediaOptions.MaximumImagePixels}; it is {options.MaxImagePixels}.");
        }

        ValidateVariants(options.Variants, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateVariants(VariantOptions? variants, List<string> failures)
    {
        if (variants is null)
        {
            failures.Add("Media:Variants must not be empty.");
            return;
        }

        var widths = variants.Widths;
        if (widths.Length is 0 or > VariantOptions.MaximumWidthCount)
        {
            failures.Add($"Media:Variants:Widths must list between 1 and {VariantOptions.MaximumWidthCount} widths.");
        }
        else if (widths.Any(w => w is < VariantOptions.MinimumWidth or > VariantOptions.MaximumWidth))
        {
            failures.Add(
                $"Media:Variants:Widths must each be between {VariantOptions.MinimumWidth} and " +
                $"{VariantOptions.MaximumWidth} pixels.");
        }
        else if (widths.Zip(widths.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            failures.Add("Media:Variants:Widths must be in ascending order with no repeats.");
        }

        if (variants.MaxConcurrent is < 1 or > VariantOptions.MaximumConcurrency)
        {
            failures.Add(
                $"Media:Variants:MaxConcurrent must be between 1 and {VariantOptions.MaximumConcurrency}; " +
                $"it is {variants.MaxConcurrent}.");
        }

        if (variants.Timeout < VariantOptions.MinimumTimeout || variants.Timeout > VariantOptions.MaximumTimeout)
        {
            failures.Add(
                $"Media:Variants:Timeout must be between {VariantOptions.MinimumTimeout} and " +
                $"{VariantOptions.MaximumTimeout}; it is {variants.Timeout}.");
        }
    }
}
