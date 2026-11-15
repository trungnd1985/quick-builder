using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Trash;

/// <summary>A filter over one kind's trash (021 FR-010, TR-19).</summary>
internal sealed record TrashQueryOptions(string? Search, Guid? TrashedBy, DateTimeOffset? From, DateTimeOffset? To, string? After, int Limit);

/// <summary>
/// What the Pages trash slices share: reading trashed pages and components, describing them,
/// and planning a restore (021 TR-10, TR-19).
/// </summary>
/// <remarks>
/// The only place in Pages besides the purge that reads trashed rows; every read names its
/// reason (TR-03). Restore authority mirrors delete: <c>pages.structure.manage</c> for pages,
/// <c>pages.components.manage</c> for components (FR-013).
/// </remarks>
internal sealed class PagesTrash(
    PagesDbContext database,
    PagesAccess access,
    IAccessEvaluator capabilities,
    ICollectionSchemaReader collections,
    TrashRetention retention,
    TimeProvider clock)
{
    internal const int PageSize = 50;
    private const string Reason = "The site's trash: list, preview and restore (021 TR-01).";

    internal IQueryable<Page> TrashedPages(Guid siteId) => database.ForSite<Page>(siteId).OnlyTrash(Reason);

    internal IQueryable<Component> TrashedComponents(Guid siteId) => database.ForSite<Component>(siteId).OnlyTrash(Reason);

    internal Task<bool> MayAsync(string capability, Guid siteId, CancellationToken cancellationToken) =>
        capabilities.HasCapabilityAsync(capability, siteId, cancellationToken);

    internal static string RestoreCapability(string kind) =>
        kind == TrashKinds.Page ? PagesCapabilities.StructureManage : PagesCapabilities.ComponentsManage;

    /// <summary>Lists one kind's trash, newest first, keyset-paged (TR-19).</summary>
    internal async Task<Result<TrashListResponse>> ListAsync(Guid siteId, string kind, TrashQueryOptions options, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(options.Limit, 1, PageSize);
        var cursor = Cursor.Parse(options.After);

        var canRestore = await MayAsync(RestoreCapability(kind), siteId, cancellationToken).ConfigureAwait(false);
        var canPurge = await MayAsync(PagesCapabilities.TrashPurge, siteId, cancellationToken).ConfigureAwait(false);

        List<TrashRow> rows;
        int total;
        if (kind == TrashKinds.Page)
        {
            var query = TrashedPages(siteId);
            if (!string.IsNullOrWhiteSpace(options.Search))
            {
                var pattern = Like(options.Search);
                query = query.Where(p => EF.Functions.ILike(p.Title, pattern) || EF.Functions.ILike(p.TrashedPath!, pattern));
            }

            query = Filter(query, options, p => p.TrashedBy, p => p.TrashedAtUtc);
            total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            if (cursor is { } c)
            {
                query = query.Where(p => p.TrashedAtUtc < c.At || (p.TrashedAtUtc == c.At && p.Id.CompareTo(c.Id) < 0));
            }

            rows = await query
                .OrderByDescending(p => p.TrashedAtUtc).ThenByDescending(p => p.Id)
                .Take(limit + 1)
                .Select(p => new TrashRow(p.Id, p.Title, p.TrashedPath, p.TrashedParentId, p.TrashedAtUtc!.Value, p.TrashedBy!.Value, false, p.TrashedHadSchedule == true))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var query = TrashedComponents(siteId);
            if (!string.IsNullOrWhiteSpace(options.Search))
            {
                var pattern = Like(options.Search);
                query = query.Where(c => EF.Functions.ILike(c.Name, pattern));
            }

            query = Filter(query, options, c => c.TrashedBy, c => c.TrashedAtUtc);
            total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            if (cursor is { } c)
            {
                query = query.Where(x => x.TrashedAtUtc < c.At || (x.TrashedAtUtc == c.At && x.Id.CompareTo(c.Id) < 0));
            }

            rows = await query
                .OrderByDescending(x => x.TrashedAtUtc).ThenByDescending(x => x.Id)
                .Take(limit + 1)
                .Select(x => new TrashRow(x.Id, x.Name, null, null, x.TrashedAtUtc!.Value, x.TrashedBy!.Value, x.TrashedWasLive == true, x.TrashedHadSchedule == true))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var more = rows.Count > limit;
        rows = [.. rows.Take(limit)];
        var entries = await DescribeAsync(siteId, kind, rows, canRestore, canPurge, cancellationToken).ConfigureAwait(false);
        var next = more ? new Cursor(rows[^1].TrashedAtUtc, rows[^1].Id).ToString() : null;
        return Result.Success(new TrashListResponse(entries, next, total));
    }

    /// <summary>One entry with its draft, for the preview (FR-011).</summary>
    internal async Task<Result<TrashEntryDetailResponse>> GetAsync(Guid siteId, string kind, Guid id, CancellationToken cancellationToken)
    {
        TrashRow? row;
        string? draft;
        if (kind == TrashKinds.Page)
        {
            row = await TrashedPages(siteId).Where(p => p.Id == id)
                .Select(p => new TrashRow(p.Id, p.Title, p.TrashedPath, p.TrashedParentId, p.TrashedAtUtc!.Value, p.TrashedBy!.Value, false, p.TrashedHadSchedule == true))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            draft = row is null ? null : await database.ForSite<PageDraft>(siteId).Where(d => d.PageId == id).Select(d => d.Document).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            row = await TrashedComponents(siteId).Where(c => c.Id == id)
                .Select(c => new TrashRow(c.Id, c.Name, null, null, c.TrashedAtUtc!.Value, c.TrashedBy!.Value, c.TrashedWasLive == true, c.TrashedHadSchedule == true))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            draft = row is null ? null : await database.ForSite<ComponentDraft>(siteId).Where(d => d.ComponentId == id).Select(d => d.Document).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        if (row is null)
        {
            return Result.Failure<TrashEntryDetailResponse>(PagesErrors.NotInTrash);
        }

        var canRestore = await MayAsync(RestoreCapability(kind), siteId, cancellationToken).ConfigureAwait(false);
        var canPurge = await MayAsync(PagesCapabilities.TrashPurge, siteId, cancellationToken).ConfigureAwait(false);
        var entries = await DescribeAsync(siteId, kind, [row], canRestore, canPurge, cancellationToken).ConfigureAwait(false);
        return Result.Success(new TrashEntryDetailResponse(entries[0], draft));
    }

    /// <summary>
    /// What restoring a page will do (TR-10). <paramref name="placement"/> null means "original",
    /// which is refused with <c>trash.placement_required</c> when the parent is not live.
    /// </summary>
    internal async Task<Result<(RestorePlanResponse Plan, PageRestoreOutcome Outcome, IReadOnlyList<Page> Ancestors)>> PlanPageAsync(
        PageTree tree, Page page, string? placement, Guid? chosenParentId, bool tracked, CancellationToken cancellationToken)
    {
        static Result<(RestorePlanResponse, PageRestoreOutcome, IReadOnlyList<Page>)> Refuse(Error error) =>
            Result.Failure<(RestorePlanResponse, PageRestoreOutcome, IReadOnlyList<Page>)>(error);

        var siteId = tree.SiteId;
        var parentLive = page.TrashedParentId is not { } original || tree.Pages.Any(p => p.Id == original);
        var chain = parentLive ? [] : await TrashedAncestorsAsync(tree, page, tracked, cancellationToken).ConfigureAwait(false);

        // FR-016: parents in the trash can come back too; a parent that is gone leaves only a choice.
        var options = parentLive
            ? new List<string> { RestorePlacements.Original }
            : chain.Count > 0
                ? [RestorePlacements.WithParents, RestorePlacements.Chosen, RestorePlacements.Top]
                : [RestorePlacements.Chosen, RestorePlacements.Top];

        var parents = new List<RestoreParentResponse>();
        var ancestors = new List<Page>();
        Guid? parentId;
        int? position;
        switch (placement ?? RestorePlacements.Original)
        {
            case RestorePlacements.Original when parentLive:
                parentId = page.TrashedParentId;
                position = page.TrashedPosition;
                break;
            case RestorePlacements.Original:
                return Refuse(PagesErrors.PlacementRequired);
            case RestorePlacements.WithParents when chain.Count > 0:
                // Top-down, each to its own place, so the addresses shown are the ones it will have.
                foreach (var ancestor in Enumerable.Reverse(chain))
                {
                    var planned = tree.PlanRestore(
                        ancestor,
                        ancestor.TrashedParentId,
                        ancestor.TrashedPosition,
                        await CollectionExistsAsync(ancestor, cancellationToken).ConfigureAwait(false));
                    if (planned.IsFailure)
                    {
                        return Refuse(planned.Error);
                    }

                    var restored = tree.Restore(ancestor, planned.Value, access.Actor, clock);
                    if (restored.IsFailure)
                    {
                        return Refuse(restored.Error);
                    }

                    parents.Add(new RestoreParentResponse(ancestor.Id, ancestor.Title, planned.Value.Path));
                    ancestors.Add(ancestor);
                }

                parentId = page.TrashedParentId;
                position = page.TrashedPosition;
                break;
            case RestorePlacements.Top:
                parentId = null;
                position = null;
                break;
            case RestorePlacements.Chosen when chosenParentId is { } chosen:
                parentId = chosen;
                position = null;
                break;
            default:
                return Refuse(PagesErrors.InvalidPlacement);
        }

        var outcome = tree.PlanRestore(page, parentId, position, await CollectionExistsAsync(page, cancellationToken).ConfigureAwait(false));
        if (outcome.IsFailure)
        {
            return Refuse(outcome.Error);
        }

        var o = outcome.Value;
        var parentLabel = o.ParentId is { } p ? tree.Pages.First(x => x.Id == p).Title : string.Empty;
        var canPublish = await CanRestoreAndPublishAsync(siteId, SubjectKinds.Page, page.Id, cancellationToken).ConfigureAwait(false);
        var plan = new RestorePlanResponse(
            placement ?? RestorePlacements.Original,
            options,
            new RestoreTargetResponse(o.ParentId, parentLabel, o.Position),
            parents,
            o.Path,
            page.TrashedPath,
            o.Path != page.TrashedPath,
            o.BecomesHome,
            page.TrashedTemplateCollectionId is not null && o.TemplateCollectionId is null,
            WasLive: false,
            page.TrashedHadSchedule == true,
            canPublish);
        return Result.Success<(RestorePlanResponse, PageRestoreOutcome, IReadOnlyList<Page>)>((plan, o, ancestors));
    }

    private async Task<bool> CollectionExistsAsync(Page page, CancellationToken cancellationToken) =>
        page.TrashedTemplateCollectionId is { } template
        && (await collections.ExistingAsync(page.SiteId, [template], cancellationToken).ConfigureAwait(false)).Contains(template);

    /// <summary>
    /// The trashed pages above <paramref name="page"/>, nearest first, ending at a live parent or
    /// the top level. Empty when a parent on the way is gone: then they cannot all come back.
    /// </summary>
    private async Task<List<Page>> TrashedAncestorsAsync(PageTree tree, Page page, bool tracked, CancellationToken cancellationToken)
    {
        var chain = new List<Page>();
        var query = tracked ? TrashedPages(tree.SiteId) : TrashedPages(tree.SiteId).AsNoTracking();
        for (var next = page.TrashedParentId; next is { } id && !tree.Pages.Any(p => p.Id == id);)
        {
            var parent = await query.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
            if (parent is null || chain.Count >= PageAddresses.MaxDepth)
            {
                return [];
            }

            chain.Add(parent);
            next = parent.TrashedParentId;
        }

        return chain;
    }

    /// <summary>What restoring a component will do: its name, or a numbered one when taken (FR-017).</summary>
    internal async Task<RestorePlanResponse> PlanComponentAsync(Component component, CancellationToken cancellationToken)
    {
        var name = await FreeComponentNameAsync(component, cancellationToken).ConfigureAwait(false);
        var canPublish = await CanRestoreAndPublishAsync(component.SiteId, SubjectKinds.Component, component.Id, cancellationToken).ConfigureAwait(false);
        return new RestorePlanResponse(
            RestorePlacements.Original,
            [RestorePlacements.Original],
            new RestoreTargetResponse(null, string.Empty, 0),
            [],
            name,
            component.Name,
            !string.Equals(name, component.Name, StringComparison.Ordinal),
            BecomesHome: false,
            TemplateDropped: false,
            component.TrashedWasLive == true,
            component.TrashedHadSchedule == true,
            canPublish);
    }

    internal async Task<string> FreeComponentNameAsync(Component component, CancellationToken cancellationToken)
    {
        var taken = await database.ForSite<Component>(component.SiteId)
            .Where(c => c.Id != component.Id)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return CopyNaming.Unique(component.Name, taken, Component.MaxNameLength);
    }

    /// <summary>The number of the last version that was live, or null when it never was (TR-11).</summary>
    internal async Task<int?> LastLiveVersionAsync(Guid siteId, string subjectKind, Guid subjectId, CancellationToken cancellationToken) =>
        await database.ForSite<PublishedVersion>(siteId)
            .Where(v => v.SubjectKind == subjectKind && v.SubjectId == subjectId)
            .OrderByDescending(v => v.Number)
            .Select(v => (int?)v.Number)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<bool> CanRestoreAndPublishAsync(Guid siteId, string subjectKind, Guid subjectId, CancellationToken cancellationToken) =>
        await MayAsync(PublishingCapabilities.Manage, siteId, cancellationToken).ConfigureAwait(false)
        && await LastLiveVersionAsync(siteId, subjectKind, subjectId, cancellationToken).ConfigureAwait(false) is not null;

    private async Task<List<TrashEntryResponse>> DescribeAsync(
        Guid siteId, string kind, IReadOnlyList<TrashRow> rows, bool canRestore, bool canPurge, CancellationToken cancellationToken)
    {
        var names = await access.NamesAsync(rows.Select(r => r.TrashedBy), cancellationToken).ConfigureAwait(false);

        // FR-005: where a page was, and whether that place is live, in the trash or gone.
        var parentIds = rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value).Distinct().ToList();
        var parents = parentIds.Count == 0
            ? []
            : await database.ForSite<Page>(siteId).IncludingTrash(Reason)
                .Where(p => parentIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Title, Trashed = p.TrashedAtUtc != null })
                .ToDictionaryAsync(p => p.Id, cancellationToken)
                .ConfigureAwait(false);

        return [.. rows.Select(r =>
        {
            TrashPlaceResponse? place = null;
            if (kind == TrashKinds.Page)
            {
                place = r.ParentId is not { } parentId
                    ? new TrashPlaceResponse(null, null, "none")
                    : parents.TryGetValue(parentId, out var parent)
                        ? new TrashPlaceResponse(parentId, parent.Title, parent.Trashed ? "trashed" : "live")
                        : new TrashPlaceResponse(parentId, null, "gone");
            }

            return new TrashEntryResponse(
                kind,
                r.Id,
                r.Label,
                r.Address,
                place,
                r.TrashedAtUtc,
                new TrashActorResponse(r.TrashedBy, names.GetValueOrDefault(r.TrashedBy) ?? string.Empty),
                retention.PurgeOn(r.TrashedAtUtc),
                r.WasLive,
                r.HadSchedule,
                canRestore,
                canPurge);
        })];
    }

    private static IQueryable<T> Filter<T>(
        IQueryable<T> query,
        TrashQueryOptions options,
        System.Linq.Expressions.Expression<Func<T, Guid?>> by,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset?>> at)
    {
        if (options.TrashedBy is { } who)
        {
            query = query.Where(Compose(by, v => v == who));
        }

        if (options.From is { } from)
        {
            query = query.Where(Compose(at, v => v >= from));
        }

        if (options.To is { } to)
        {
            query = query.Where(Compose(at, v => v <= to));
        }

        return query;
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> Compose<T, TValue>(
        System.Linq.Expressions.Expression<Func<T, TValue>> selector,
        System.Linq.Expressions.Expression<Func<TValue, bool>> predicate)
    {
        var body = new Replace(predicate.Parameters[0], selector.Body).Visit(predicate.Body)!;
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    private static string Like(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private sealed class Replace(System.Linq.Expressions.ParameterExpression from, System.Linq.Expressions.Expression to)
        : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }

    private sealed record TrashRow(
        Guid Id, string Label, string? Address, Guid? ParentId, DateTimeOffset TrashedAtUtc, Guid TrashedBy, bool WasLive, bool HadSchedule);

    /// <summary>A keyset position: the last entry's trash time and id.</summary>
    private readonly record struct Cursor(DateTimeOffset At, Guid Id)
    {
        public static Cursor? Parse(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var parts = text.Split('_');
            return parts.Length == 2
                   && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                   && Guid.TryParse(parts[1], out var id)
                ? new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }

        public override string ToString() => $"{At.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{Id:N}";
    }
}
