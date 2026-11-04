using System.Text.RegularExpressions;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// The rules of the site archive container, format 1 (020 contracts/site-archive.md §1, §6).
/// </summary>
/// <remarks>
/// Entry names are the security boundary of an import: only the four shapes below are
/// accepted, and nothing is ever extracted to disk by name (research ST-02).
/// </remarks>
internal static partial class ArchiveFormat
{
    public const int CurrentFormat = 1;

    public const string ManifestEntry = "manifest.json";
    public const string AuthorsEntry = "authors.jsonl";
    public const string FileExtension = ".qbsite";
    public const string MediaType = "application/zip";

    /// <summary>The formats this release reads.</summary>
    public static readonly IReadOnlySet<int> SupportedFormats = new HashSet<int> { 1 };

    /// <summary>The longest <c>manifest.json</c> read: it lists every entry, about 120 bytes each.</summary>
    public const long MaxManifestBytes = 256L * 1024 * 1024;

    /// <summary>The longest line of a <c>.jsonl</c> entry. A page document is at most a few MB (page-schema §4).</summary>
    public const int MaxLineBytes = 32 * 1024 * 1024;

    public static string LinesEntry(string part, string entry) => $"parts/{part}/{entry}.jsonl";

    public static string MediaEntry(Guid fileId) => $"media/{fileId:N}";

    /// <summary>Whether <paramref name="name"/> is one of the four entry shapes.</summary>
    public static bool IsAllowedName(string name) => EntryName().IsMatch(name);

    /// <summary>Whether a part or entry segment is valid.</summary>
    public static bool IsSegment(string value) => Segment().IsMatch(value);

    /// <summary>
    /// The release majors whose archives this release reads (research ST-21). Only its own
    /// today; a later major lists the earlier ones its upgrade notes support.
    /// </summary>
    public static bool IsSupportedRelease(SemanticVersion archive, SemanticVersion current) => archive.Major == current.Major;

    [GeneratedRegex(@"\A(?:manifest\.json|authors\.jsonl|parts/[a-z0-9-]{1,64}/[a-z0-9-]{1,64}\.jsonl|media/[0-9a-f]{32})\z", RegexOptions.CultureInvariant)]
    private static partial Regex EntryName();

    [GeneratedRegex(@"\A[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();
}
