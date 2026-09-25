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

    /// <summary>The allowed types as <see cref="MediaFileType"/>s. Valid only after validation.</summary>
    internal IReadOnlyList<MediaFileType> AllowedFileTypes =>
        [.. AllowedTypes.Select(MediaFileType.FromMime).OfType<MediaFileType>().Distinct()];
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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
