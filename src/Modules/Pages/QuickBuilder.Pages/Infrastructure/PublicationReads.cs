using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The publication summaries the page tree, the component list and the publishing endpoints
/// return (012 FR-007, data-model §1.6).
/// </summary>
/// <remarks>
/// One query per table for any number of subjects, so the tree of a 2,000-page site costs the
/// same handful of queries as one page. "Unpublished changes" is computed from versions, never
/// by comparing documents (research PR-02).
/// </remarks>
internal sealed class PublicationReads(PagesDbContext database, PagesAccess access)
{
    /// <summary>Summaries for <paramref name="subjectIds"/> of one kind; subjects never published get <see cref="PublicationResponse.Draft"/>.</summary>
    public async Task<IReadOnlyDictionary<Guid, PublicationResponse>> SummariesAsync(
        Guid siteId, string subjectKind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken)
    {
        if (subjectIds.Count == 0)
        {
            return new Dictionary<Guid, PublicationResponse>();
        }

        var publications = await database.ForSite<Publication>(siteId)
            .AsNoTracking()
            .Where(p => p.SubjectKind == subjectKind && subjectIds.Contains(p.SubjectId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var schedules = await database.ForSite<PublishSchedule>(siteId)
            .AsNoTracking()
            .Where(s => s.SubjectKind == subjectKind
                        && subjectIds.Contains(s.SubjectId)
                        && (s.State == ScheduleStates.Pending || s.State == ScheduleStates.Failed))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (publications.Count == 0 && schedules.Count == 0)
        {
            return subjectIds.ToDictionary(id => id, _ => PublicationResponse.Draft);
        }

        var draftVersions = await DraftVersionsAsync(siteId, subjectKind, subjectIds, cancellationToken).ConfigureAwait(false);

        var liveVersionIds = publications.Where(p => p.LiveVersionId is not null).Select(p => p.LiveVersionId!.Value).ToList();
        var liveTitles = subjectKind == SubjectKinds.Page && liveVersionIds.Count > 0
            ? await database.ForSite<PublishedVersion>(siteId)
                .Where(v => liveVersionIds.Contains(v.Id))
                .Select(v => new { v.SubjectId, v.Title })
                .ToDictionaryAsync(v => v.SubjectId, v => v.Title, cancellationToken)
                .ConfigureAwait(false)
            : [];

        var pages = subjectKind == SubjectKinds.Page
            ? await database.ForSite<Page>(siteId)
                .Where(p => subjectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Title, p.Path })
                .ToDictionaryAsync(p => p.Id, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var names = await access.NamesAsync(
                publications.Where(p => p.LiveBy is not null).Select(p => p.LiveBy!.Value).Concat(schedules.Select(s => s.CreatedBy)),
                cancellationToken)
            .ConfigureAwait(false);

        var baseUrl = subjectKind == SubjectKinds.Page ? await access.BaseUrlAsync(siteId, cancellationToken).ConfigureAwait(false) : null;

        var byId = publications.ToDictionary(p => p.SubjectId);
        var schedulesById = schedules.ToLookup(s => s.SubjectId);

        return subjectIds.ToDictionary(id => id, id =>
        {
            var subjectSchedules = schedulesById[id].ToList();
            var pending = subjectSchedules
                .Where(s => s.IsPending)
                .OrderBy(s => s.DueAtUtc)
                .Select(s => Schedule(s, names))
                .ToList();
            var failed = subjectSchedules
                .Where(s => s.State == ScheduleStates.Failed)
                .OrderByDescending(s => s.CompletedAtUtc)
                .Select(s => Schedule(s, names))
                .FirstOrDefault();

            if (!byId.TryGetValue(id, out var publication) || !publication.IsLive)
            {
                return PublicationResponse.Draft with { PendingSchedules = pending, FailedSchedule = failed };
            }

            var page = pages?.GetValueOrDefault(id);
            var changed = publication.HasUnpublishedChanges(
                draftVersions.GetValueOrDefault(id), page?.Title, liveTitles.GetValueOrDefault(id));

            return new PublicationResponse(
                "published",
                publication.LiveNumber,
                publication.LiveSinceUtc,
                publication.LiveBy is { } by ? PagesAccess.Reference(by, names) : null,
                changed,
                page is null || baseUrl is null ? null : LiveUrl(baseUrl, page.Path),
                pending,
                failed);
        });
    }

    /// <summary>The summary for one subject.</summary>
    public async Task<PublicationResponse> SummaryAsync(Guid siteId, string subjectKind, Guid subjectId, CancellationToken cancellationToken) =>
        (await SummariesAsync(siteId, subjectKind, [subjectId], cancellationToken).ConfigureAwait(false))[subjectId];

    /// <summary>A schedule as the API shows it.</summary>
    public static ScheduleResponse Schedule(PublishSchedule schedule, IReadOnlyDictionary<Guid, string> names)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new ScheduleResponse(
            schedule.Action,
            schedule.DueAtUtc,
            schedule.State,
            schedule.FailureCode,
            PagesAccess.Reference(schedule.CreatedBy, names),
            schedule.CapturedDraftVersion);
    }

    private static string LiveUrl(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

    private async Task<Dictionary<Guid, long>> DraftVersionsAsync(
        Guid siteId, string subjectKind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        // xmin is read as it is and widened here: PostgreSQL has no cast from xid to bigint.
        if (subjectKind == SubjectKinds.Page)
        {
            var pages = await database.ForSite<PageDraft>(siteId)
                .Where(d => ids.Contains(d.PageId))
                .Select(d => new { d.PageId, d.Version })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return pages.ToDictionary(d => d.PageId, d => (long)d.Version);
        }

        // 024 research NV-02: a menu's draft is its own table, as a component's is.
        if (subjectKind == SubjectKinds.Menu)
        {
            var menus = await database.ForSite<Domain.Menus.MenuDraft>(siteId)
                .Where(d => ids.Contains(d.MenuId))
                .Select(d => new { d.MenuId, d.Version })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return menus.ToDictionary(d => d.MenuId, d => (long)d.Version);
        }

        var components = await database.ForSite<ComponentDraft>(siteId)
            .Where(d => ids.Contains(d.ComponentId))
            .Select(d => new { d.ComponentId, d.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return components.ToDictionary(d => d.ComponentId, d => (long)d.Version);
    }
}
