using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>
/// <c>manifest.json</c> of a site archive, format 1 (020 contracts/site-archive.md §2).
/// </summary>
/// <remarks>
/// Parts read it to learn which contract versions the archive's documents use. Reading and
/// checking it (entry names, hashes, versions) belongs to the Sites module.
/// </remarks>
public sealed record ArchiveManifest
{
    public required int Format { get; init; }

    /// <summary>The semantic version of the release that wrote the archive.</summary>
    public required string Release { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required ArchiveSource Source { get; init; }

    public required IReadOnlyList<ArchivePartInfo> Parts { get; init; }

    public required IReadOnlyDictionary<string, long> Counts { get; init; }

    public required IReadOnlyList<ArchiveProblem> Problems { get; init; }

    /// <summary>Every entry except <c>manifest.json</c>, with its length and SHA-256 (lower-case hex).</summary>
    public required IReadOnlyList<ArchiveEntryInfo> Entries { get; init; }

    /// <summary>The highest version of <paramref name="contract"/> any part declares, or null.</summary>
    public int? ContractVersion(string contract) =>
        Parts.Select(p => p.Contracts.TryGetValue(contract, out var v) ? v : (int?)null).Max();

    /// <summary>The part named <paramref name="name"/>, or null when the archive has none.</summary>
    public ArchivePartInfo? Part(string name) => Parts.FirstOrDefault(p => p.Name == name);
}

/// <summary>The site an archive was exported from.</summary>
public sealed record ArchiveSource(Guid SiteId, string SiteName, bool Enabled);

/// <summary>One part's entry in the manifest.</summary>
public sealed record ArchivePartInfo(string Name, int Version, IReadOnlyDictionary<string, int> Contracts);

/// <summary>One entry of the archive with its length and SHA-256.</summary>
public sealed record ArchiveEntryInfo(string Name, long Length, string Sha256);

/// <summary>A problem found while exporting, repeated at check (FR-009).</summary>
public sealed record ArchiveProblem(string Code, string Kind, Guid Id);

/// <summary>The JSON settings every archive entry is written and read with.</summary>
public static class ArchiveJson
{
    /// <summary>camelCase, enums as camelCase strings, no indentation (one object per line).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
