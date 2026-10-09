using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Reads items into the response shapes of contracts/items-api.yaml (015), with their pending
/// schedules and the names of the members involved.
/// </summary>
/// <remarks>Admin reads for a site named by the caller, always through <see cref="CmsDbContext.ForSite{T}"/>.</remarks>
internal sealed class ItemReads(CmsDbContext database, CmsAccess access)
{
    /// <summary>One item of a collection, or null when it does not exist there.</summary>
    public async Task<ItemResponse?> GetAsync(Guid siteId, Guid collectionId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await database.ForSite<Item>(siteId)
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == itemId && i.CollectionId == collectionId, cancellationToken)
            .ConfigureAwait(false);

        return item is null ? null : await ToResponseAsync(item, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The response for an item, read as it is now; <paramref name="unpublishedReferences"/> only after a publish.</summary>
    public async Task<ItemResponse> ToResponseAsync(Item item, IReadOnlyList<ItemRefResponse> unpublishedReferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var schemaRevision = await database.ForSite<Collection>(item.SiteId)
            .AsNoTracking()
            .Where(c => c.Id == item.CollectionId)
            .Select(c => c.SchemaRevision)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var schedules = await PendingAsync(item.SiteId, [item.Id], cancellationToken).ConfigureAwait(false);
        var mine = schedules.GetValueOrDefault(item.Id) ?? [];
        var names = await access.NamesAsync(mine.Select(s => s.CreatedBy).Append(item.UpdatedBy), cancellationToken).ConfigureAwait(false);

        var draft = item.DraftValues();
        var captured = mine.FirstOrDefault(s => s.Action == ItemScheduleActions.Publish);
        var differs = captured?.ReadCaptured() is { } capturedValues
            && !ItemStatuses.SameContent(capturedValues, captured.CapturedSlug, draft, item.Slug);

        var report = ItemReport.Read(item.Attention);
        return new ItemResponse(
            item.Id,
            item.CollectionId,
            item.Name,
            item.Slug,
            item.Status,
            !report.IsEmpty,
            mine.Select(s => Schedule(s, names)).ToList(),
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            Member(item.UpdatedBy, names),
            item.PublishedAtUtc,
            item.Revision,
            schemaRevision,
            draft.Write(),
            item.PublishedValues()?.Write(),
            item.PublishedSlug,
            item.LiveVersion,
            Report(report),
            differs,
            unpublishedReferences);
    }

    /// <summary>List rows for items already read, with their pending schedules and updaters' names.</summary>
    public async Task<IReadOnlyList<ItemSummaryResponse>> SummariesAsync(Guid siteId, IReadOnlyList<ItemRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var schedules = await PendingAsync(siteId, rows.Select(r => r.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var names = await access.NamesAsync(
                rows.Select(r => r.UpdatedBy).Concat(schedules.Values.SelectMany(s => s).Select(s => s.CreatedBy)),
                cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new ItemSummaryResponse(
                r.Id,
                r.Name,
                r.Slug,
                r.Status,
                r.Attention,
                (schedules.GetValueOrDefault(r.Id) ?? []).Select(s => Schedule(s, names)).ToList(),
                r.CreatedAtUtc,
                r.UpdatedAtUtc,
                Member(r.UpdatedBy, names),
                r.PublishedAtUtc))
            .ToList();
    }

    public static ReportResponse Report(ItemReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ReportResponse(report.Problems.Select(p => new ProblemResponse(p.FieldId, p.Problem, p.Limit, p.Format, p.Path)).ToList());
    }

    public static ItemRefResponse Ref(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ItemRefResponse(item.Id, item.CollectionId, item.Name, item.Slug, item.Status);
    }

    private async Task<Dictionary<Guid, List<ItemSchedule>>> PendingAsync(Guid siteId, List<Guid> itemIds, CancellationToken cancellationToken) =>
        (await database.ForSite<ItemSchedule>(siteId)
            .AsNoTracking()
            .Where(s => itemIds.Contains(s.ItemId) && s.State == ItemScheduleStates.Pending)
            .OrderBy(s => s.DueAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
        .GroupBy(s => s.ItemId)
        .ToDictionary(g => g.Key, g => g.ToList());

    private static ScheduleResponse Schedule(ItemSchedule schedule, IReadOnlyDictionary<Guid, string> names) =>
        new(schedule.Action, schedule.DueAtUtc, Member(schedule.CreatedBy, names));

    private static MemberResponse Member(Guid id, IReadOnlyDictionary<Guid, string> names) =>
        new(id, names.GetValueOrDefault(id, string.Empty));
}

/// <summary>What the list reads of an item: its columns, never its values documents.</summary>
internal sealed record ItemRow(
    Guid Id,
    string? Name,
    string Slug,
    string Status,
    bool Attention,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy,
    DateTimeOffset? PublishedAtUtc);

/// <summary>Reads the values document a request sends (contract §3).</summary>
internal static class ItemInput
{
    /// <summary>
    /// The values document, or null when it is not one: not an object, a duplicate property
    /// name, a missing or unsupported <c>schemaVersion</c>, or <c>values</c> that is not an
    /// object or holds a <c>null</c>. Each value's own shape is <c>ItemValidation</c>'s to judge.
    /// </summary>
    public static ItemValues? ReadValues(System.Text.Json.JsonElement? element)
    {
        if (element is not { ValueKind: System.Text.Json.JsonValueKind.Object } document
            || Schema.CollectionSchemaValidator.FindDuplicateProperty(document, string.Empty) is not null
            || !document.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != System.Text.Json.JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number != QuickBuilder.Cms.Contracts.CollectionSchema.CurrentVersion
            || !document.TryGetProperty("values", out var values)
            || values.ValueKind != System.Text.Json.JsonValueKind.Object
            || values.EnumerateObject().Any(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Null))
        {
            return null;
        }

        return ItemValues.Read(document);
    }

    /// <summary>A report saying the values document itself is not one.</summary>
    public static ItemReport MalformedDocument { get; } = new([new ItemProblem("values", ItemProblems.Malformed)]);

    /// <summary>Just the values, keyed by field id, as JSON for a response.</summary>
    public static JsonObject ValuesOf(ItemValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Write()["values"]!.AsObject();
    }
}
