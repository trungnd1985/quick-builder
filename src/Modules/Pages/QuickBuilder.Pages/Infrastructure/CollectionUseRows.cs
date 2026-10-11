using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Keeps <c>pages.collection_uses</c> in step with a subject's draft, live version and pending
/// scheduled content (016 data-model §1.3, research TB-13).
/// </summary>
/// <remarks>
/// <see cref="StageAsync"/> rewrites one subject and state as tracked changes, so they commit
/// with the caller's own save, in its transaction, like draft media references: usage and the
/// change it follows land together or not at all. The schemas it reads tell multi-reference
/// targets; they come from the admin-side reader, never the public one.
/// </remarks>
internal sealed class CollectionUseRows(PagesDbContext database, ICollectionSchemaReader collections)
{
    /// <summary>
    /// Makes the subject's rows for <paramref name="state"/> exactly what
    /// <paramref name="document"/> and <paramref name="templateCollectionId"/> use; no document
    /// and no template remove them.
    /// </summary>
    public async Task StageAsync(
        Guid siteId,
        string subjectKind,
        Guid subjectId,
        string state,
        string? document,
        Guid? templateCollectionId,
        CancellationToken cancellationToken)
    {
        var wanted = await ExtractAsync(siteId, document, templateCollectionId, cancellationToken).ConfigureAwait(false);

        var existing = await database.ForSite<CollectionUsage>(siteId)
            .Where(u => u.SubjectKind == subjectKind && u.SubjectId == subjectId && u.State == state)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in existing)
        {
            if (!wanted.TryGetValue(row.CollectionId, out var fields) || !fields.SetEquals(row.FieldIds))
            {
                database.CollectionUsages.Remove(row);
            }
        }

        foreach (var (collectionId, fields) in wanted)
        {
            if (!existing.Exists(r => r.CollectionId == collectionId && fields.SetEquals(r.FieldIds)))
            {
                database.CollectionUsages.Add(new CollectionUsage(siteId, subjectKind, subjectId, state, collectionId, fields));
            }
        }
    }

    /// <summary>
    /// Restages a page's draft, live and scheduled rows from what is stored, with
    /// <paramref name="templateCollectionId"/> as its template: a template change touches every
    /// state at once.
    /// </summary>
    public async Task RestagePageAsync(Guid siteId, Guid pageId, Guid? templateCollectionId, CancellationToken cancellationToken)
    {
        var draft = await database.ForSite<PageDraft>(siteId)
            .Where(d => d.PageId == pageId)
            .Select(d => d.Document)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var liveVersionId = await database.ForSite<Publication>(siteId)
            .Where(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == pageId)
            .Select(p => p.LiveVersionId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var live = liveVersionId is { } versionId
            ? await database.ForSite<PublishedVersion>(siteId)
                .Where(v => v.Id == versionId)
                .Select(v => v.Document)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var scheduled = await database.ForSite<PublishSchedule>(siteId)
            .Where(s => s.SubjectKind == SubjectKinds.Page && s.SubjectId == pageId
                        && s.State == ScheduleStates.Pending && s.Action == ScheduleActions.Publish)
            .Select(s => s.CapturedDocument)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        await StageAsync(siteId, SubjectKinds.Page, pageId, CollectionUsageStates.Draft, draft, templateCollectionId, cancellationToken).ConfigureAwait(false);
        await StageAsync(siteId, SubjectKinds.Page, pageId, CollectionUsageStates.Live, live, live is null ? null : templateCollectionId, cancellationToken)
            .ConfigureAwait(false);
        await StageAsync(siteId, SubjectKinds.Page, pageId, CollectionUsageStates.Scheduled, scheduled, scheduled is null ? null : templateCollectionId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes every row of a subject that is being deleted; the caller saves.</summary>
    public Task ForgetAsync(Guid siteId, string subjectKind, Guid subjectId, CancellationToken cancellationToken) =>
        database.ForSite<CollectionUsage>(siteId)
            .Where(u => u.SubjectKind == subjectKind && u.SubjectId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// The uses of a document, reading schemas as the walk meets collections: a list over a
    /// multi-reference field needs its target, and a nested one its target's target, so the walk
    /// runs again while it finds collections whose schemas it has not read (at most three times).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, SortedSet<string>>> ExtractAsync(
        Guid siteId, string? document, Guid? templateCollectionId, CancellationToken cancellationToken)
    {
        var schemas = new Dictionary<Guid, JsonElement>();
        Guid? Target(Guid collection, string fieldId) =>
            schemas.TryGetValue(collection, out var schema) ? ReferenceTarget(schema, fieldId) : null;

        var uses = CollectionUseExtractor.Extract(document, templateCollectionId, Target);
        for (var pass = 0; pass < 3; pass++)
        {
            var unread = uses.Keys.Where(id => !schemas.ContainsKey(id)).ToList();
            if (unread.Count == 0)
            {
                break;
            }

            foreach (var (id, schema) in await collections.GetSchemasAsync(siteId, unread, cancellationToken).ConfigureAwait(false))
            {
                schemas[id] = schema;
            }

            // A collection that is gone is still a use: it keeps its row, so the draft stays found.
            foreach (var id in unread.Where(id => !schemas.ContainsKey(id)))
            {
                schemas[id] = default;
            }

            uses = CollectionUseExtractor.Extract(document, templateCollectionId, Target);
        }

        return uses;
    }

    private static Guid? ReferenceTarget(JsonElement schema, string fieldId)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var field in fields.EnumerateArray())
        {
            if (field.TryGetProperty("id", out var id) && id.GetString() == fieldId
                && field.TryGetProperty("type", out var type) && type.GetString() is "reference" or "multiReference"
                && field.TryGetProperty("target", out var target) && Guid.TryParse(target.GetString(), out var targetId))
            {
                return targetId;
            }
        }

        return null;
    }
}
