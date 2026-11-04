using System.Text.Json;

namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>The kinds of id an archive carries (research ST-08). Each is owned by one part.</summary>
public static class ArchiveKinds
{
    public const string Site = "site";
    public const string HostName = "host_name";
    public const string MediaFile = "media_file";
    public const string Folder = "folder";
    public const string Design = "design";
    public const string Collection = "collection";
    public const string Item = "item";
    public const string Page = "page";
    public const string Component = "component";
    public const string Menu = "menu";
    public const string Redirect = "redirect";
}

/// <summary>How an archive id found its target (data-model §3).</summary>
public enum MatchedBy
{
    Identity,
    Address,
    Name,
    Slug,
    Source,
    Content,
}

/// <summary>What happens to one archive id on import (data-model §6).</summary>
public enum IdOutcome
{
    /// <summary>Written under a new id (new site, or no match in a merge).</summary>
    New,

    /// <summary>Written under its original id (restore).</summary>
    Original,

    /// <summary>The target's document takes the archive's content (merge, replace).</summary>
    Replaced,

    /// <summary>Not written; references point to the target's document (merge, skip).</summary>
    Skipped,

    /// <summary>Written under a new id and a unique name beside the target's (merge, rename).</summary>
    Renamed,

    /// <summary>Not written; an identical target file is used (media).</summary>
    Reused,
}

/// <summary>The complete, read-only id map an import applies with.</summary>
public interface IIdMap
{
    /// <summary>The id <paramref name="archiveId"/> of <paramref name="kind"/> has in the target, or null when the archive does not hold it.</summary>
    Guid? Resolve(string kind, Guid archiveId);

    /// <summary>What happens to it, or null when the archive does not hold it.</summary>
    IdOutcome? Outcome(string kind, Guid archiveId);

    /// <summary>
    /// The target id of a reference, or the archive id unchanged when the map does not hold it:
    /// such a reference stays broken, as in the source (spec edge case). Records it in
    /// <paramref name="broken"/> when given.
    /// </summary>
    Guid Remap(string kind, Guid archiveId, Action<string, Guid>? broken = null);

    /// <summary>
    /// A document (page, component, menu, item values, design tokens) with every id the archive
    /// declares replaced by its target id, as JSON text to store (research ST-09). Ids are
    /// random UUIDs, so a string equal to a declared id is a reference to it; everything else,
    /// node and field ids included, is kept as it is.
    /// </summary>
    string RemapDocument(JsonElement document);
}

/// <summary>How parts declare and match their ids during a check.</summary>
public interface IIdMapBuilder
{
    /// <summary>
    /// Declares an archive id. Unless matched, it gets a new id, or keeps its original id in a
    /// restore. <paramref name="label"/> is shown to the Administrator only.
    /// </summary>
    void Declare(string kind, Guid archiveId, string label);

    /// <summary>Matches a declared id to a target document; the merge policy decides the outcome.</summary>
    void Match(string kind, Guid archiveId, Guid targetId, MatchedBy matchedBy);

    /// <summary>Marks a declared media file as identical to a target file, so it is reused.</summary>
    void Reuse(string kind, Guid archiveId, Guid targetId);

    /// <summary>Whether the archive declares <paramref name="archiveId"/> of <paramref name="kind"/>.</summary>
    bool IsDeclared(string kind, Guid archiveId);

    /// <summary>
    /// Adds a fact about the target that the fingerprint must cover, such as a document a restore
    /// removes, so the apply refuses when it changed since the check (research ST-06).
    /// </summary>
    void Note(string fact);
}

/// <summary>Maps the member ids an archive names to ids on this installation (research ST-14).</summary>
public interface IAuthorMap
{
    /// <summary>A user here, or a historical author created for the archive's member.</summary>
    Guid Map(Guid archiveUserId);

    /// <summary>As <see cref="Map(Guid)"/> for optional author columns.</summary>
    Guid? Map(Guid? archiveUserId);
}

/// <summary>Collects the member ids a part's rows name, so the export can list their display names.</summary>
public interface IAuthorCollector
{
    void Add(Guid? userId);
}
