using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// A file's address under <c>/_media</c>: an original or a variant of one content version
/// (013 research MV-08, contracts/public-media.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Exactly one canonical form per resource:
/// <list type="bullet">
/// <item><c>{id}.{ext}</c>: the original of version 1, unchanged from 003;</item>
/// <item><c>{id}-v{n}.{ext}</c>: the original of version n ≥ 2;</item>
/// <item><c>{id}-v{n}-w{width}.{ext}</c>: a variant, which always names its version.</item>
/// </list>
/// Anything else, including <c>-v1</c> on an original or a width that is not configured, does
/// not parse, so it is answered exactly like an unknown id.
/// </para>
/// <para>
/// Because a replacement changes the version and so the address, the bytes behind any one
/// address never change, which is what lets every address be cached as immutable.
/// </para>
/// </remarks>
internal readonly partial record struct MediaAddress(Guid FileId, int Version, int? Width, string Extension)
{
    /// <summary>The public prefix, before any base path (003 research M-11).</summary>
    public const string Prefix = "/_media/";

    /// <summary>Whether this addresses a variant rather than an original.</summary>
    public bool IsVariant => Width is not null;

    /// <summary>
    /// Parses the part of a request path after <c>/_media/</c>, or returns null when it is not
    /// a canonical address with a configured width.
    /// </summary>
    public static MediaAddress? TryParse(string? file, IReadOnlySet<int> widths)
    {
        ArgumentNullException.ThrowIfNull(widths);

        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        var match = Pattern().Match(file);
        if (!match.Success)
        {
            return null;
        }

        var id = Guid.ParseExact(match.Groups["id"].Value, "N");
        var extension = match.Groups["ext"].Value;
        var versionGroup = match.Groups["v"];
        var widthGroup = match.Groups["w"];

        if (!versionGroup.Success)
        {
            // "{id}.{ext}" is version 1's original; there is no unversioned variant form.
            return new MediaAddress(id, 1, null, extension);
        }

        var version = int.Parse(versionGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture);

        if (!widthGroup.Success)
        {
            // Version 1's original has exactly one address, without "-v1".
            return version >= 2 ? new MediaAddress(id, version, null, extension) : null;
        }

        var width = int.Parse(widthGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture);
        return widths.Contains(width) ? new MediaAddress(id, version, width, extension) : null;
    }

    /// <summary>The original of <paramref name="file"/>'s current version.</summary>
    public static MediaAddress Original(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new MediaAddress(file.Id, file.ContentVersion, null, file.Extension);
    }

    /// <summary>A variant of <paramref name="file"/>'s current version.</summary>
    public static MediaAddress Variant(MediaFile file, int width)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        return new MediaAddress(file.Id, file.ContentVersion, width, file.Extension);
    }

    /// <summary>The file part of the address, without <c>/_media/</c>.</summary>
    public string FileName =>
        (Width, Version) switch
        {
            (null, 1) => $"{FileId:N}.{Extension}",
            (null, _) => $"{FileId:N}-v{Version.ToString(CultureInfo.InvariantCulture)}.{Extension}",
            ({ } w, _) => $"{FileId:N}-v{Version.ToString(CultureInfo.InvariantCulture)}-w{w.ToString(CultureInfo.InvariantCulture)}.{Extension}",
        };

    /// <summary>The address relative to the base path, e.g. <c>/_media/7d41….jpg</c>.</summary>
    public string ToPath() => Prefix + FileName;

    /// <summary>
    /// The strong <c>ETag</c> value (without quotes): what the address serves, exactly
    /// (contracts/public-media.md §1.3). Version 1's original keeps 003's <c>{id}</c>.
    /// </summary>
    public string ETagValue => FileName[..FileName.LastIndexOf('.')];

    public override string ToString() => ToPath();

    // What the server issues: 32 lower-case hex digits, an optional version (no leading zero,
    // at most five digits), an optional width (only after a version), a dot and a lower-case
    // extension. Traversal, encodings, upper case and extra segments never match.
    [GeneratedRegex(
        "^(?<id>[0-9a-f]{32})(?:-v(?<v>[1-9][0-9]{0,4})(?:-w(?<w>[1-9][0-9]{1,3}))?)?\\.(?<ext>[a-z0-9]{1,8})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
