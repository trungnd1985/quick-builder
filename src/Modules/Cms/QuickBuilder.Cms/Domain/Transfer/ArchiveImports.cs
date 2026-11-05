using System.Text.Json;

namespace QuickBuilder.Cms.Domain;

// 020 FR-021: the Cms rows of an imported site, as the archive holds them. Each factory takes
// a row the import's check has already validated, under the id the import's id map gives it,
// and documents already remapped into the target site (research ST-08, ST-09). Derived index
// rows (slugs, references, media uses, search text) are not set here: ItemIndexer rebuilds
// them, as after any write (research ST-03).

/// <summary>An imported collection.</summary>
internal sealed partial class Collection
{
    public static Collection FromArchive(
        Guid id,
        Guid siteId,
        string name,
        string slug,
        string? description,
        string schema,
        int schemaVersion,
        int revision,
        int schemaRevision,
        int nextFieldNumber,
        int nextOptionNumber,
        DateTimeOffset createdAtUtc,
        Guid createdBy,
        DateTimeOffset updatedAtUtc,
        Guid updatedBy) => new()
        {
            Id = id,
            SiteId = siteId,
            Name = name,
            Slug = slug,
            Description = description,
            Schema = schema,
            SchemaVersion = schemaVersion,
            Revision = revision,
            SchemaRevision = schemaRevision,
            NextFieldNumber = nextFieldNumber,
            NextOptionNumber = nextOptionNumber,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = updatedAtUtc,
            UpdatedBy = updatedBy,
        };
}

/// <summary>The fields of an imported item.</summary>
internal sealed record ImportedItem(
    Guid Id,
    Guid CollectionId,
    string Slug,
    string Status,
    string Draft,
    int Revision,
    int DraftRevision,
    Guid? DraftChangedBy,
    DateTimeOffset DraftChangedAtUtc,
    string DraftChangeKind,
    string? Published,
    string? PublishedSlug,
    DateTimeOffset? PublishedAtUtc,
    Guid? PublishedBy,
    DateTimeOffset? FirstPublishedAtUtc,
    int? LiveVersion,
    int NextVersionNumber,
    CopyOriginFields? CopyOrigin,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy);

/// <summary>The copy origin of an imported item (023 FR-021).</summary>
internal sealed record CopyOriginFields(Guid SourceId, DateTimeOffset SourceSavedAtUtc, Guid CopiedBy, DateTimeOffset CopiedAtUtc);

/// <summary>An imported item.</summary>
internal sealed partial class Item
{
    public static Item FromArchive(Guid siteId, ImportedItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new Item
        {
            Id = row.Id,
            SiteId = siteId,
            CollectionId = row.CollectionId,
            Slug = row.Slug,
            Status = row.Status,
            Draft = JsonDocument.Parse(row.Draft),
            Revision = row.Revision,
            DraftRevision = row.DraftRevision,
            DraftChangedBy = row.DraftChangedBy,
            DraftChangedAtUtc = row.DraftChangedAtUtc,
            DraftChangeKind = row.DraftChangeKind,
            Published = row.Published is null ? null : JsonDocument.Parse(row.Published),
            PublishedSlug = row.PublishedSlug,
            PublishedAtUtc = row.PublishedAtUtc,
            PublishedBy = row.PublishedBy,
            FirstPublishedAtUtc = row.FirstPublishedAtUtc,
            LiveVersion = row.LiveVersion,
            NextVersionNumber = row.NextVersionNumber,
            CopiedFromId = row.CopyOrigin?.SourceId,
            CopiedFromSavedAtUtc = row.CopyOrigin?.SourceSavedAtUtc,
            CopiedBy = row.CopyOrigin?.CopiedBy,
            CopiedAtUtc = row.CopyOrigin?.CopiedAtUtc,
            SearchText = string.Empty,
            CreatedAtUtc = row.CreatedAtUtc,
            CreatedBy = row.CreatedBy,
            UpdatedAtUtc = row.UpdatedAtUtc,
            UpdatedBy = row.UpdatedBy,
        };
    }
}

/// <summary>An imported pending schedule.</summary>
internal sealed partial class ItemSchedule
{
    public static ItemSchedule FromArchive(
        Guid id,
        Guid siteId,
        Guid itemId,
        string action,
        DateTimeOffset dueAtUtc,
        int revision,
        string? capturedValues,
        string? capturedSlug,
        bool redirectOldAddress,
        Guid? redirectToPageId,
        Guid createdBy,
        DateTimeOffset createdAtUtc) => new()
        {
            Id = id,
            SiteId = siteId,
            ItemId = itemId,
            Action = action,
            DueAtUtc = dueAtUtc,
            Revision = revision,
            State = ItemScheduleStates.Pending,
            CapturedValues = capturedValues is null ? null : JsonDocument.Parse(capturedValues),
            CapturedSlug = capturedSlug,
            RedirectOldAddress = redirectOldAddress,
            RedirectToPageId = redirectToPageId,
            CreatedBy = createdBy,
            CreatedAtUtc = createdAtUtc,
        };
}

/// <summary>An imported history entry.</summary>
internal sealed partial class ItemHistoryEntry
{
    public static ItemHistoryEntry FromArchive(
        Guid id,
        Guid siteId,
        Guid itemId,
        string kind,
        int? versionNumber,
        string? scheduleAction,
        DateTimeOffset? dueAtUtc,
        string? failureCode,
        DateTimeOffset atUtc,
        Guid by) => new()
        {
            Id = id,
            SiteId = siteId,
            ItemId = itemId,
            Kind = kind,
            VersionNumber = versionNumber,
            ScheduleAction = scheduleAction,
            DueAtUtc = dueAtUtc,
            FailureCode = failureCode,
            AtUtc = atUtc,
            By = by,
        };

    /// <summary>Start fresh (FR-021a): the item's whole history is one entry, created by the importer.</summary>
    public static ItemHistoryEntry CreatedByImport(Guid siteId, Guid itemId, Guid by, DateTimeOffset at) =>
        new(siteId, itemId, ItemHistoryKinds.Created, by, at);
}

/// <summary>An imported schema revision.</summary>
internal sealed partial class SchemaChangeEntry
{
    public static SchemaChangeEntry FromArchive(Guid collectionId, int revision, Guid siteId, DateTimeOffset changedAtUtc, Guid changedBy, string summary) =>
        new(collectionId, revision, siteId, changedAtUtc, changedBy, summary);
}
