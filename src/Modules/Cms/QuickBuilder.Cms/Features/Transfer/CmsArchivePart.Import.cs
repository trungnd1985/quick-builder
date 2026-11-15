using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Cms.Features.Transfer;

/// <summary>Checking and applying the Cms part (020 FR-019, FR-021 … FR-027, FR-029; research ST-03, ST-10, ST-11).</summary>
internal sealed partial class CmsArchivePart
{
    private const int Batch = 500;

    public async Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // FR-014: every schema against the rules a stored schema must pass; item values must be
        // value documents, and slugs valid address names.
        var validator = services.GetRequiredService<QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaValidator>();
        await foreach (var c in context.Archive.ReadLinesAsync<CollectionRow>(PartName, "collections", cancellationToken).ConfigureAwait(false))
        {
            var result = validator.Validate(c.Schema, SchemaDocumentKind.Stored);
            if (!result.IsValid)
            {
                context.Report.Error(
                    c.SchemaVersion > QuickBuilder.Cms.Contracts.CollectionSchema.CurrentVersion ? "transfer.document_too_new" : "transfer.document_invalid",
                    new TransferSubject(ArchiveKinds.Collection, c.Id, c.Name), result.Code ?? "The collection schema is not valid.");
            }
        }

        await foreach (var i in context.Archive.ReadLinesAsync<ItemRow>(PartName, "items", cancellationToken).ConfigureAwait(false))
        {
            var subject = new TransferSubject(ArchiveKinds.Item, i.Id, i.Name ?? i.Slug);
            foreach (var values in new[] { (JsonElement?)i.Draft, i.Published })
            {
                if (values is { } v && (v.ValueKind != JsonValueKind.Object || !v.TryGetProperty("values", out var inner) || inner.ValueKind != JsonValueKind.Object))
                {
                    context.Report.Error("transfer.document_invalid", subject, "The item values are not a values document.");
                }
            }
        }

        if (context.Session is { } session)
        {
            database.JoinTransfer(session.Connection, session.Transaction);
        }

        var collectionRows = await Lines<CollectionRow>(context.Archive, "collections", cancellationToken).ConfigureAwait(false);
        foreach (var c in collectionRows)
        {
            context.Ids.Declare(ArchiveKinds.Collection, c.Id, c.Name);
        }

        long items = 0;
        await foreach (var i in context.Archive.ReadLinesAsync<ItemRow>(PartName, "items", cancellationToken).ConfigureAwait(false))
        {
            context.Ids.Declare(ArchiveKinds.Item, i.Id, i.Name ?? i.Slug);
            items++;
        }

        if (context.Mode == ImportMode.Merge)
        {
            await MatchAsync(context, collectionRows, cancellationToken).ConfigureAwait(false);
        }

        // FR-029: a schedule whose time has passed is dropped, and said so before confirming.
        await foreach (var s in context.Archive.ReadLinesAsync<ItemScheduleRow>(PartName, "item-schedules", cancellationToken).ConfigureAwait(false))
        {
            if (s.DueAtUtc <= context.NowUtc)
            {
                context.Report.PastSchedule(new TransferSubject(ArchiveKinds.Item, s.ItemId, s.CapturedSlug ?? s.ItemId.ToString()), s.DueAtUtc, s.Action);
            }
        }

        context.Report.Count(ArchiveKinds.Collection, collectionRows.Count);
        context.Report.Count(ArchiveKinds.Item, items);

        if (context.Mode == ImportMode.Restore)
        {
            // FR-027b: what a restore removes.
            foreach (var c in await database.Collections.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!context.Ids.IsDeclared(ArchiveKinds.Collection, c.Id))
                {
                    context.Report.Removal(new TransferSubject(ArchiveKinds.Collection, c.Id, c.Name));
                    context.Ids.Note($"remove:collection:{c.Id:N}");
                }
            }

            foreach (var i in await database.Items.AsNoTracking().Select(i => new { i.Id, i.Slug, i.Name }).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!context.Ids.IsDeclared(ArchiveKinds.Item, i.Id))
                {
                    context.Report.Removal(new TransferSubject(ArchiveKinds.Item, i.Id, i.Name ?? i.Slug));
                    context.Ids.Note($"remove:item:{i.Id:N}");
                }
            }
        }
    }

    public async Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        var site = context.TargetSiteId;
        if (context.Mode == ImportMode.Restore)
        {
            // Research ST-12: the site's Cms rows go, derived ones included; the archive's come back.
            context.Report.Removed(ArchiveKinds.Item, await database.Items.CountAsync(cancellationToken).ConfigureAwait(false));
            await database.ItemMedia.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.ItemReferences.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.ItemSlugs.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.ItemHistory.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.ItemSchedules.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.ItemVersions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            // 021 TR-17: a restore import empties the trash with the rest of the site.
            await database.Items.IncludingTrash("A restore import replaces the whole site, its trash included (021 TR-17).").ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.CollectionReferences.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.SchemaChanges.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.Collections.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        var ids = context.Ids;
        var authors = context.Authors;
        var fresh = context.StartFresh;
        var merge = context.Mode == ImportMode.Merge;

        // Collections first: items are validated and indexed against their schemas.
        var touched = new List<Guid>();
        var added = new HashSet<Guid>();
        var notFitting = new HashSet<Guid>();
        var replacements = new List<(Guid TargetId, CollectionRow Row)>();
        var targets = merge
            ? await database.Collections.AsNoTracking().Select(c => new { c.Id, c.Slug, c.Name }).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
        var takenSlugs = targets.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);
        var takenNames = targets.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long collectionCount = 0;
        await foreach (var c in context.Archive.ReadLinesAsync<CollectionRow>(PartName, "collections", cancellationToken).ConfigureAwait(false))
        {
            var subject = new TransferSubject(ArchiveKinds.Collection, c.Id, c.Name);
            var id = ids.Remap(ArchiveKinds.Collection, c.Id);
            switch (ids.Outcome(ArchiveKinds.Collection, c.Id))
            {
                case IdOutcome.Skipped:
                    context.Report.Resolved(subject, id, IdOutcome.Skipped);
                    if (!await FitsAsync(id, c, cancellationToken).ConfigureAwait(false))
                    {
                        notFitting.Add(id);
                    }

                    touched.Add(id);
                    continue;

                case IdOutcome.Replaced:
                    // After the added collections, which its reference fields may point to.
                    replacements.Add((id, c));
                    touched.Add(id);
                    continue;
            }

            var name = c.Name;
            var slug = c.Slug;
            if (ids.Outcome(ArchiveKinds.Collection, c.Id) == IdOutcome.Renamed)
            {
                // FR-026, research ST-10: address name "-2"…, display name "(imported)".
                slug = takenSlugs.Contains(slug) ? ConflictRules.NextSlug(slug, takenSlugs.Contains) : slug;
                name = takenNames.Contains(name) ? ConflictRules.ImportedName(name, takenNames.Contains) : name;
                context.Report.Resolved(subject, id, IdOutcome.Renamed, name);
            }

            takenSlugs.Add(slug);
            takenNames.Add(name);
            var collection = Collection.FromArchive(
                id, site, name, slug, c.Description, ids.RemapDocument(c.Schema), c.SchemaVersion,
                c.Revision, c.SchemaRevision, c.NextFieldNumber, c.NextOptionNumber,
                c.CreatedAtUtc, authors.Map(c.CreatedBy), c.UpdatedAtUtc, authors.Map(c.UpdatedBy));
            database.Collections.Add(collection);
            database.CollectionReferences.AddRange(CollectionReference.For(collection));
            touched.Add(id);
            added.Add(id);
            collectionCount++;
        }

        await foreach (var change in context.Archive.ReadLinesAsync<SchemaChangeRow>(PartName, "schema-changes", cancellationToken).ConfigureAwait(false))
        {
            var collection = ids.Remap(ArchiveKinds.Collection, change.CollectionId);
            if (added.Contains(collection))
            {
                database.SchemaChanges.Add(SchemaChangeEntry.FromArchive(
                    collection, change.Revision, site, change.ChangedAtUtc, authors.Map(change.ChangedBy), change.Summary.GetRawText()));
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
        context.Report.Imported(ArchiveKinds.Collection, collectionCount);

        foreach (var (id, row) in replacements)
        {
            var subject = new TransferSubject(ArchiveKinds.Collection, row.Id, row.Name);
            if (await ReplaceCollectionAsync(context, id, row, cancellationToken).ConfigureAwait(false))
            {
                context.Report.Resolved(subject, id, IdOutcome.Replaced);
                continue;
            }

            // The member's rules refuse this schema change (014): the target is kept, as the
            // check warned.
            context.Report.Resolved(subject, id, IdOutcome.Skipped);
            context.Report.LeftOut(subject, "schema_change_refused");
            if (!await FitsAsync(id, row, cancellationToken).ConfigureAwait(false))
            {
                notFitting.Add(id);
            }
        }

        database.ChangeTracker.Clear();

        // Items, then their versions, schedules and history.
        var takenItemSlugs = merge
            ? (await database.Items.AsNoTracking().Select(i => new { i.CollectionId, i.Slug }).ToListAsync(cancellationToken).ConfigureAwait(false))
                .GroupBy(i => i.CollectionId)
                .ToDictionary(g => g.Key, g => g.Select(i => i.Slug).ToHashSet(StringComparer.Ordinal))
            : [];
        var withHistory = new HashSet<Guid>();
        long imported = 0;
        await foreach (var i in context.Archive.ReadLinesAsync<ItemRow>(PartName, "items", cancellationToken).ConfigureAwait(false))
        {
            var id = ids.Remap(ArchiveKinds.Item, i.Id);
            var collectionId = ids.Remap(ArchiveKinds.Collection, i.CollectionId);
            var subject = new TransferSubject(ArchiveKinds.Item, i.Id, i.Name ?? i.Slug);
            var outcome = ids.Outcome(ArchiveKinds.Item, i.Id);
            if (outcome == IdOutcome.Skipped)
            {
                context.Report.Resolved(subject, id, IdOutcome.Skipped);
                continue;
            }

            if (outcome == IdOutcome.Replaced)
            {
                await ReplaceItemAsync(context, id, i, cancellationToken).ConfigureAwait(false);
                context.Report.Resolved(subject, id, IdOutcome.Replaced);
                continue;
            }

            if (notFitting.Contains(collectionId))
            {
                // FR-025: the target's collection is kept, and this item's values do not fit it.
                context.Report.LeftOut(subject, "values_do_not_fit");
                continue;
            }

            var slug = i.Slug;
            var slugs = takenItemSlugs.TryGetValue(collectionId, out var known) ? known : takenItemSlugs[collectionId] = new HashSet<string>(StringComparer.Ordinal);
            if (slugs.Contains(slug))
            {
                // FR-026, research ST-10: slug "-2"… (015 rules).
                slug = ConflictRules.NextSlug(slug, slugs.Contains);
                context.Report.Resolved(subject, id, IdOutcome.Renamed, slug);
            }
            else if (outcome == IdOutcome.Renamed)
            {
                // Its collection was renamed too, so its own slug is free there.
                context.Report.Resolved(subject, id, IdOutcome.Renamed, slug);
            }

            slugs.Add(slug);
            var draft = ids.RemapDocument(fresh && i.Published is { } live && i.Status == ItemStatuses.Published ? live : i.Draft);
            database.Items.Add(Item.FromArchive(site, new ImportedItem(
                id,
                collectionId,
                slug,
                fresh ? ItemStatuses.Draft : i.Status,
                draft,
                fresh ? 1 : i.Revision,
                fresh ? 1 : i.DraftRevision,
                fresh ? context.ActorUserId : authors.Map(i.DraftChangedBy),
                fresh ? context.NowUtc : i.DraftChangedAtUtc,
                i.DraftChangeKind,
                fresh || i.Published is not { } published ? null : ids.RemapDocument(published),
                fresh ? null : i.PublishedSlug is null ? null : slug,
                fresh ? null : i.PublishedAtUtc,
                fresh ? null : authors.Map(i.PublishedBy),
                fresh ? null : i.FirstPublishedAtUtc,
                fresh ? null : i.LiveVersion,
                fresh ? 1 : i.NextVersionNumber,
                fresh || i.CopiedFromId is not { } source ? null : new CopyOriginFields(ids.Remap(ArchiveKinds.Item, source), i.CopiedFromSavedAtUtc!.Value, authors.Map(i.CopiedBy!.Value), i.CopiedAtUtc!.Value),
                i.CreatedAtUtc,
                fresh ? context.ActorUserId ?? authors.Map(i.CreatedBy) : authors.Map(i.CreatedBy),
                fresh ? context.NowUtc : i.UpdatedAtUtc,
                fresh ? context.ActorUserId ?? authors.Map(i.UpdatedBy) : authors.Map(i.UpdatedBy))));
            withHistory.Add(i.Id);

            if (fresh)
            {
                database.ItemHistory.Add(ItemHistoryEntry.CreatedByImport(site, id, context.ActorUserId ?? authors.Map(i.CreatedBy), context.NowUtc));
            }

            if (++imported % Batch == 0)
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
        context.Report.Imported(ArchiveKinds.Item, imported);

        if (!fresh)
        {
            // A replaced item keeps its own history; the archive's goes with the items it adds.
            await ApplyHistoryAsync(context, site, withHistory, cancellationToken).ConfigureAwait(false);
        }

        // Derived rows from the values, as after any write (research ST-03).
        await IndexAsync(touched, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Matches the archive's collections and items to the target's (FR-022, FR-022a): from the
    /// same site by identity first, otherwise collections by address name and items by address
    /// name within their matched collection. A replace's schema change is previewed (FR-024).
    /// </summary>
    private async Task MatchAsync(PartCheckContext context, List<CollectionRow> collectionRows, CancellationToken cancellationToken)
    {
        var targets = await database.Collections.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = targets.ToDictionary(c => c.Id);
        var bySlug = targets.ToDictionary(c => c.Slug, StringComparer.Ordinal);
        var matched = new Dictionary<Guid, Guid>();
        foreach (var c in collectionRows)
        {
            var subject = new TransferSubject(ArchiveKinds.Collection, c.Id, c.Name);
            Collection? target = null;
            MatchedBy by = MatchedBy.Slug;
            if (context.SameSiteAsTarget && byId.TryGetValue(c.Id, out var same))
            {
                (target, by) = (same, MatchedBy.Identity);
            }
            else if (bySlug.TryGetValue(c.Slug, out var bySlugMatch))
            {
                target = bySlugMatch;
            }

            if (target is null)
            {
                continue;
            }

            context.Ids.Match(ArchiveKinds.Collection, c.Id, target.Id, by);
            var clash = by == MatchedBy.Identity && bySlug.TryGetValue(c.Slug, out var other) && other.Id != target.Id
                ? new TransferSubject(ArchiveKinds.Collection, other.Id, other.Name)
                : null;
            context.Report.Conflict(subject, target.Id, by, clash);
            context.Ids.Note($"collection:{target.Id:N}:{target.Revision}");
            matched[c.Id] = target.Id;
        }

        if (matched.Count == 0)
        {
            return;
        }

        foreach (var c in collectionRows.Where(c => matched.ContainsKey(c.Id)))
        {
            await PreviewReplaceAsync(context, byId[matched[c.Id]], c, matched, cancellationToken).ConfigureAwait(false);
        }

        var targetCollections = matched.Values.ToHashSet();
        var targetItems = await database.Items.AsNoTracking()
            .Where(i => targetCollections.Contains(i.CollectionId))
            .Select(i => new { i.Id, i.CollectionId, i.Slug, i.Name, i.Revision })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var itemsById = targetItems.ToDictionary(i => i.Id);
        var itemsBySlug = targetItems.ToDictionary(i => (i.CollectionId, i.Slug));
        await foreach (var i in context.Archive.ReadLinesAsync<ItemRow>(PartName, "items", cancellationToken).ConfigureAwait(false))
        {
            if (!matched.TryGetValue(i.CollectionId, out var collection))
            {
                continue;
            }

            var subject = new TransferSubject(ArchiveKinds.Item, i.Id, i.Name ?? i.Slug);
            var by = MatchedBy.Slug;
            var target = context.SameSiteAsTarget && itemsById.TryGetValue(i.Id, out var same) && same.CollectionId == collection ? same : null;
            if (target is not null)
            {
                by = MatchedBy.Identity;
            }
            else if (itemsBySlug.TryGetValue((collection, i.Slug), out var slugMatch))
            {
                target = slugMatch;
            }

            if (target is null)
            {
                continue;
            }

            context.Ids.Match(ArchiveKinds.Item, i.Id, target.Id, by);
            var clash = by == MatchedBy.Identity && itemsBySlug.TryGetValue((collection, i.Slug), out var other) && other.Id != target.Id
                ? new TransferSubject(ArchiveKinds.Item, other.Id, other.Name ?? other.Slug)
                : null;
            context.Report.Conflict(subject, target.Id, by, clash);
            context.Ids.Note($"item:{target.Id:N}:{target.Revision}");
        }
    }

    /// <summary>FR-024: what replacing <paramref name="target"/> with the archive's fields would do to its items.</summary>
    /// <remarks>
    /// The check has no complete map yet: a reference to a matched collection points to its
    /// match, and one to a collection the import adds points to the collection itself, which
    /// is always a known target. The apply evaluates again with the complete map.
    /// </remarks>
    private async Task PreviewReplaceAsync(
        PartCheckContext context, Collection target, CollectionRow row, Dictionary<Guid, Guid> matched, CancellationToken cancellationToken)
    {
        var declared = await Lines<CollectionRow>(context.Archive, "collections", cancellationToken).ConfigureAwait(false);
        var local = declared.ToDictionary(c => c.Id, c => matched.TryGetValue(c.Id, out var m) ? m : target.Id);
        var evaluation = await EvaluateReplaceAsync(schema => RemapIds(schema, local), target, row, cancellationToken).ConfigureAwait(false);
        if (evaluation is null)
        {
            context.Report.Warning(
                "transfer.collection_not_replaceable",
                new TransferSubject(ArchiveKinds.Collection, row.Id, row.Name),
                "Its fields cannot replace the existing collection's under the rules of a schema change. Under replace it is kept as it is.");
            return;
        }

        foreach (var change in evaluation.Impact.Changes.Where(c => c.Change.TouchesValues))
        {
            context.Report.SchemaChange(
                target.Id,
                change.Change.FieldId ?? string.Empty,
                change.Change.Kind.ToString(),
                change.CountOf(ValueOutcome.Converted),
                change.CountOf(ValueOutcome.Cleared) + change.CountOf(ValueOutcome.PartlyLost));
        }
    }

    /// <summary>
    /// The archive's fields as a member's save of <paramref name="target"/> would take them, with
    /// their references rewritten by <paramref name="remap"/>, or null when that save would be
    /// refused (014).
    /// </summary>
    private async Task<Evaluation?> EvaluateReplaceAsync(Func<JsonElement, string> remap, Collection target, CollectionRow row, CancellationToken cancellationToken)
    {
        var schema = JsonDocument.Parse(remap(row.Schema)).RootElement.Clone();
        var request = new SaveCollectionRequest(target.Revision, target.Name, target.Slug, row.Description, schema, FillIns: null, Confirmation: null);
        var evaluated = await services.GetRequiredService<ProposalEvaluator>().EvaluateAsync(target, request, cancellationToken).ConfigureAwait(false);
        return evaluated.IsFailure ? null : evaluated.Value;
    }

    /// <summary>
    /// FR-024: the target collection keeps its id, address name and display name, and takes the
    /// archive's fields through 014's schema change, which converts or clears its items' values.
    /// </summary>
    private async Task<bool> ReplaceCollectionAsync(PartApplyContext context, Guid targetId, CollectionRow row, CancellationToken cancellationToken)
    {
        var collection = await database.Collections.SingleAsync(c => c.Id == targetId, cancellationToken).ConfigureAwait(false);
        var evaluation = await EvaluateReplaceAsync(context.Ids.RemapDocument, collection, row, cancellationToken).ConfigureAwait(false);
        if (evaluation is null)
        {
            return false;
        }

        if (evaluation.Changes.Count == 0)
        {
            return true;
        }

        var clock = services.GetRequiredService<TimeProvider>();
        var actor = context.ActorUserId ?? Guid.Empty;
        await services.GetRequiredService<ItemMigrator>()
            .MigrateAsync(collection, evaluation.Plans, evaluation.Assignment.Document, reindexAll: true, cancellationToken)
            .ConfigureAwait(false);
        database.SchemaChanges.Add(collection.Apply(evaluation.Details, evaluation.Assignment, evaluation.Impact, actor, clock));
        var stale = await database.CollectionReferences.Where(r => r.SourceCollectionId == collection.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        database.CollectionReferences.RemoveRange(stale);
        database.CollectionReferences.AddRange(CollectionReference.For(collection));
        CmsOutbox.AddFrom(database, collection, context.ActorUserId);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// FR-024: the target item keeps its id, collection and address name, and takes the archive's
    /// draft and live state; publishing it is a new version, so its earlier versions stay.
    /// </summary>
    private async Task ReplaceItemAsync(PartApplyContext context, Guid targetId, ItemRow row, CancellationToken cancellationToken)
    {
        var item = await database.Items.SingleAsync(i => i.Id == targetId, cancellationToken).ConfigureAwait(false);
        var actor = context.ActorUserId ?? context.Authors.Map(row.UpdatedBy);
        var now = context.NowUtc;
        using (var draft = JsonDocument.Parse(context.Ids.RemapDocument(row.Draft)))
        {
            item.SaveDraft(item.Slug, ItemValues.Read(draft), actor, now);
        }

        if (row.Published is { } published && row.Status != ItemStatuses.Draft)
        {
            using var live = JsonDocument.Parse(context.Ids.RemapDocument(published));
            database.ItemVersions.Add(item.Publish(ItemValues.Read(live), item.PublishedSlug ?? item.Slug, ItemVersionKinds.Publish, null, null, actor, now));
        }
        else if (item.IsPublished)
        {
            item.Unpublish(null, actor, now);
        }

        database.ItemHistory.AddRange(item.NewHistory);
        item.ClearNewHistory();
        CmsOutbox.AddFrom(database, item, context.ActorUserId);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the archive's items of <paramref name="row"/> can be added to the target collection
    /// that is kept: every field the archive declares exists there with the same type (FR-025).
    /// </summary>
    private async Task<bool> FitsAsync(Guid targetId, CollectionRow row, CancellationToken cancellationToken)
    {
        var target = await database.Collections.AsNoTracking().SingleAsync(c => c.Id == targetId, cancellationToken).ConfigureAwait(false);
        var fields = target.SchemaDocument.Fields.ToDictionary(f => f.Id, f => f.Type, StringComparer.Ordinal);
        return CollectionSchemaDocument.Read(row.Schema).Fields.All(f => fields.TryGetValue(f.Id, out var type) && type == f.Type);
    }

    private async Task ApplyHistoryAsync(PartApplyContext context, Guid site, HashSet<Guid> items, CancellationToken cancellationToken)
    {
        var ids = context.Ids;
        var authors = context.Authors;
        long count = 0;

        await foreach (var v in context.Archive.ReadLinesAsync<ItemVersionRow>(PartName, "item-versions", cancellationToken).ConfigureAwait(false))
        {
            if (!items.Contains(v.ItemId))
            {
                continue;
            }

            using var values = JsonDocument.Parse(ids.RemapDocument(v.Values));
            database.ItemVersions.Add(new ItemVersion(
                site, ids.Remap(ArchiveKinds.Item, v.ItemId), v.Number, ItemValues.Read(values), v.Slug, v.Kind, v.RestoredFrom, authors.Map(v.PublishedBy), v.PublishedAtUtc));
            await FlushEveryAsync(++count, cancellationToken).ConfigureAwait(false);
        }

        await foreach (var s in context.Archive.ReadLinesAsync<ItemScheduleRow>(PartName, "item-schedules", cancellationToken).ConfigureAwait(false))
        {
            if (s.DueAtUtc <= context.NowUtc)
            {
                context.Report.DroppedSchedule(new TransferSubject(ArchiveKinds.Item, s.ItemId, s.CapturedSlug ?? s.ItemId.ToString()), s.DueAtUtc);
                continue;
            }

            if (!items.Contains(s.ItemId))
            {
                continue;
            }

            database.ItemSchedules.Add(ItemSchedule.FromArchive(
                Guid.CreateVersion7(), site, ids.Remap(ArchiveKinds.Item, s.ItemId), s.Action, s.DueAtUtc, s.Revision,
                s.CapturedValues is { } captured ? ids.RemapDocument(captured) : null, s.CapturedSlug, s.RedirectOldAddress,
                s.RedirectToPageId is { } page ? ids.Remap(ArchiveKinds.Page, page) : null, authors.Map(s.CreatedBy), s.CreatedAtUtc));
            await FlushEveryAsync(++count, cancellationToken).ConfigureAwait(false);
        }

        await foreach (var h in context.Archive.ReadLinesAsync<ItemHistoryRow>(PartName, "item-history", cancellationToken).ConfigureAwait(false))
        {
            if (!items.Contains(h.ItemId))
            {
                continue;
            }

            database.ItemHistory.Add(ItemHistoryEntry.FromArchive(
                Guid.CreateVersion7(), site, ids.Remap(ArchiveKinds.Item, h.ItemId), h.Kind, h.VersionNumber, h.ScheduleAction, h.DueAtUtc,
                h.FailureCode, h.AtUtc, authors.Map(h.By)));
            await FlushEveryAsync(++count, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
    }

    private async Task IndexAsync(IReadOnlyList<Guid> collections, PartApplyContext context, CancellationToken cancellationToken)
    {
        var indexer = services.GetRequiredService<ItemIndexer>();
        foreach (var collectionId in collections.Distinct())
        {
            var schema = (await database.Collections.AsNoTracking().SingleAsync(c => c.Id == collectionId, cancellationToken).ConfigureAwait(false)).SchemaDocument;
            var all = await database.Items.Where(i => i.CollectionId == collectionId).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var batch in all.Chunk(Batch))
            {
                var items = await database.Items.Where(i => batch.Contains(i.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
                await indexer.RefreshManyAsync(items, schema, versionsChanged: true, cancellationToken).ConfigureAwait(false);
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
                context.Progress.Report(0.5);
            }
        }
    }

    private async Task FlushEveryAsync(long count, CancellationToken cancellationToken)
    {
        if (count % Batch == 0)
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            database.ChangeTracker.Clear();
        }
    }

    /// <summary>A schema with every string equal to a key of <paramref name="map"/> replaced by its value.</summary>
    private static string RemapIds(JsonElement schema, Dictionary<Guid, Guid> map)
    {
        static System.Text.Json.Nodes.JsonNode? Walk(System.Text.Json.Nodes.JsonNode? node, Dictionary<Guid, Guid> map)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var (key, value) in obj.ToList())
                    {
                        var replaced = Walk(value, map);
                        if (!ReferenceEquals(replaced, value))
                        {
                            obj[key] = replaced;
                        }
                    }

                    return obj;
                case System.Text.Json.Nodes.JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        var replaced = Walk(array[i], map);
                        if (!ReferenceEquals(replaced, array[i]))
                        {
                            array[i] = replaced;
                        }
                    }

                    return array;
                case System.Text.Json.Nodes.JsonValue value when value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) && map.TryGetValue(id, out var to):
                    return System.Text.Json.Nodes.JsonValue.Create(to.ToString());
                default:
                    return node;
            }
        }

        return Walk(System.Text.Json.Nodes.JsonNode.Parse(schema.GetRawText()), map)?.ToJsonString() ?? "null";
    }

    private static async Task<List<T>> Lines<T>(IArchiveReader archive, string entry, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await foreach (var row in archive.ReadLinesAsync<T>(PartName, entry, cancellationToken).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows;
    }
}
