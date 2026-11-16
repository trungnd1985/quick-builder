using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Cms.Infrastructure.Schema;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Cms.Features.Transfer;

/// <summary>
/// The Cms module's share of an archive: collections with their schemas and schema history,
/// and items with their drafts, live values, retained versions, pending schedules, history and
/// copy origins (020 contracts/archive-parts.md §3; FR-001, FR-002).
/// </summary>
/// <remarks>
/// Item slugs, references, media uses, collection references and search text are rebuilt by
/// the module from the values, never carried (research ST-03). Values are written as stored,
/// keyed by field id, at their collection's schema version (Principle VI).
/// </remarks>
internal sealed partial class CmsArchivePart(CmsDbContext database, IServiceProvider services) : ISiteArchivePart
{
    public const string PartName = "cms";

    public string Name => PartName;

    public int Order => 40;

    public int Version => 1;

    public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>
    {
        [CollectionSchemaUpgrades.Contract] = CollectionSchema.CurrentVersion,
    };

    public async Task ExportAsync(PartExportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        var authors = context.Authors;

        context.Count(ArchiveKinds.Collection, await context.Archive.WriteLinesAsync(PartName, "collections",
            Stream(database.Collections.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id), c =>
            {
                authors.Add(c.CreatedBy);
                authors.Add(c.UpdatedBy);
                return new CollectionRow(
                    c.Id, c.Name, c.Slug, c.Description, c.SchemaVersion, Parse(c.Schema), c.Revision, c.SchemaRevision,
                    c.NextFieldNumber, c.NextOptionNumber, c.CreatedAtUtc, c.CreatedBy, c.UpdatedAtUtc, c.UpdatedBy);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        await context.Archive.WriteLinesAsync(PartName, "schema-changes",
            Stream(database.SchemaChanges.OrderBy(s => s.CollectionId).ThenBy(s => s.Revision), s =>
            {
                authors.Add(s.ChangedBy);
                return new SchemaChangeRow(s.CollectionId, s.Revision, s.ChangedAtUtc, s.ChangedBy, Parse(s.Summary));
            }, cancellationToken), cancellationToken).ConfigureAwait(false);

        var total = await database.Items.CountAsync(cancellationToken).ConfigureAwait(false);
        long done = 0;
        context.Count(ArchiveKinds.Item, await context.Archive.WriteLinesAsync(PartName, "items",
            Stream(database.Items.OrderBy(i => i.CollectionId).ThenBy(i => i.Id), i =>
            {
                authors.Add(i.CreatedBy);
                authors.Add(i.UpdatedBy);
                authors.Add(i.DraftChangedBy);
                authors.Add(i.PublishedBy);
                authors.Add(i.CopiedBy);
                context.Progress.Report(total == 0 ? 1 : 0.8 * ++done / total);
                return new ItemRow(
                    i.Id, i.CollectionId, i.Slug, i.Name, i.Status,
                    i.Draft.RootElement.Clone(), i.Revision, i.DraftRevision, i.DraftChangedBy, i.DraftChangedAtUtc, i.DraftChangeKind,
                    i.Published?.RootElement.Clone(), i.PublishedSlug, i.PublishedAtUtc, i.PublishedBy, i.FirstPublishedAtUtc,
                    i.LiveVersion, i.NextVersionNumber,
                    i.CopiedFromId, i.CopiedFromSavedAtUtc, i.CopiedBy, i.CopiedAtUtc,
                    i.CreatedAtUtc, i.CreatedBy, i.UpdatedAtUtc, i.UpdatedBy);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        context.Count("item_versions", await context.Archive.WriteLinesAsync(PartName, "item-versions",
            Stream(database.ItemVersions.OrderBy(v => v.ItemId).ThenBy(v => v.Number), v =>
            {
                authors.Add(v.PublishedBy);
                return new ItemVersionRow(v.ItemId, v.Number, v.Values.RootElement.Clone(), v.Slug, v.Kind, v.RestoredFrom, v.PublishedBy, v.PublishedAtUtc);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        // Pending schedules only (FR-002): done, failed and cancelled ones are history, kept in
        // the item history entries below.
        context.Count("item_schedules", await context.Archive.WriteLinesAsync(PartName, "item-schedules",
            Stream(database.ItemSchedules.Where(s => s.State == ItemScheduleStates.Pending).OrderBy(s => s.DueAtUtc).ThenBy(s => s.Id), s =>
            {
                authors.Add(s.CreatedBy);
                return new ItemScheduleRow(
                    s.Id, s.ItemId, s.Action, s.DueAtUtc, s.Revision, s.CapturedValues?.RootElement.Clone(), s.CapturedSlug,
                    s.RedirectOldAddress, s.RedirectToPageId, s.CreatedBy, s.CreatedAtUtc);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        await context.Archive.WriteLinesAsync(PartName, "item-history",
            Stream(database.ItemHistory.OrderBy(h => h.ItemId).ThenBy(h => h.AtUtc).ThenBy(h => h.Id), h =>
            {
                authors.Add(h.By);
                return new ItemHistoryRow(h.Id, h.ItemId, h.Kind, h.VersionNumber, h.ScheduleAction, h.DueAtUtc, h.FailureCode, h.AtUtc, h.By);
            }, cancellationToken), cancellationToken).ConfigureAwait(false);

        context.Progress.Report(1);
    }

    private static async IAsyncEnumerable<TRow> Stream<TEntity, TRow>(
        IQueryable<TEntity> query,
        Func<TEntity, TRow> map,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TEntity : class
    {
        await foreach (var entity in query.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return map(entity);
        }
    }

    private static JsonElement Parse(string json) => JsonElement.Parse(json);
}

/// <summary><c>parts/cms/collections.jsonl</c>: one collection with its schema document.</summary>
internal sealed record CollectionRow(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    int SchemaVersion,
    JsonElement Schema,
    int Revision,
    int SchemaRevision,
    int NextFieldNumber,
    int NextOptionNumber,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy);

/// <summary><c>parts/cms/schema-changes.jsonl</c>: one saved revision of a collection's schema (014 FR-033).</summary>
internal sealed record SchemaChangeRow(Guid CollectionId, int Revision, DateTimeOffset ChangedAtUtc, Guid ChangedBy, JsonElement Summary);

/// <summary><c>parts/cms/items.jsonl</c>: one item, its draft and live values, and its copy origin.</summary>
internal sealed record ItemRow(
    Guid Id,
    Guid CollectionId,
    string Slug,
    string? Name,
    string Status,
    JsonElement Draft,
    int Revision,
    int DraftRevision,
    Guid? DraftChangedBy,
    DateTimeOffset DraftChangedAtUtc,
    string DraftChangeKind,
    JsonElement? Published,
    string? PublishedSlug,
    DateTimeOffset? PublishedAtUtc,
    Guid? PublishedBy,
    DateTimeOffset? FirstPublishedAtUtc,
    int? LiveVersion,
    int NextVersionNumber,
    Guid? CopiedFromId,
    DateTimeOffset? CopiedFromSavedAtUtc,
    Guid? CopiedBy,
    DateTimeOffset? CopiedAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy);

/// <summary><c>parts/cms/item-versions.jsonl</c>: one retained published version of an item.</summary>
internal sealed record ItemVersionRow(Guid ItemId, int Number, JsonElement Values, string Slug, string Kind, int? RestoredFrom, Guid PublishedBy, DateTimeOffset PublishedAtUtc);

/// <summary><c>parts/cms/item-schedules.jsonl</c>: one pending publish or unpublish.</summary>
internal sealed record ItemScheduleRow(
    Guid Id,
    Guid ItemId,
    string Action,
    DateTimeOffset DueAtUtc,
    int Revision,
    JsonElement? CapturedValues,
    string? CapturedSlug,
    bool RedirectOldAddress,
    Guid? RedirectToPageId,
    Guid CreatedBy,
    DateTimeOffset CreatedAtUtc);

/// <summary><c>parts/cms/item-history.jsonl</c>: one entry of an item's history (015 FR-031).</summary>
internal sealed record ItemHistoryRow(
    Guid Id,
    Guid ItemId,
    string Kind,
    int? VersionNumber,
    string? ScheduleAction,
    DateTimeOffset? DueAtUtc,
    string? FailureCode,
    DateTimeOffset AtUtc,
    Guid By);
