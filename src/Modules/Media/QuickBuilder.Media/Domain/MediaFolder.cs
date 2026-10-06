using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// A named, nestable folder in one site's library (013 data-model.md §1.2, research MV-02).
/// </summary>
/// <remarks>
/// Folders only organise: a file's address never depends on its folder (FR-003). They have
/// no owner for permission purposes; <see cref="CreatedBy"/> is shown, never checked
/// (clarification). Structural rules (depth, cycles, sibling names) are enforced by
/// <see cref="FolderTree"/>, which sees the whole tree.
/// </remarks>
internal sealed class MediaFolder : ISiteOwned
{
    /// <summary>The longest folder name, after cleaning (FR-004).</summary>
    public const int MaxNameLength = 100;

    private MediaFolder()
    {
        Name = string.Empty;
        NameKey = string.Empty;
    }

    public MediaFolder(Guid id, Guid siteId, Guid? parentId, string name, DateTimeOffset createdAtUtc, Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        SiteId = siteId;
        ParentId = parentId;
        Name = name;
        NameKey = KeyFor(name);
        CreatedAtUtc = createdAtUtc;
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>The containing folder; null at the top level.</summary>
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; }

    /// <summary>The name as siblings are compared: ignoring case and accents.</summary>
    public string NameKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Display only. Grants nothing.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>How sibling names are compared (FR-004).</summary>
    public static string KeyFor(string name) => SearchText.Normalise(name);

    internal void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        NameKey = KeyFor(name);
    }

    internal void MoveTo(Guid? parentId) => ParentId = parentId;
}
