using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts.Media;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// <see cref="IMediaUsageReader"/> over the draft and version reference tables (013
/// data-model.md §2.3, research MV-05).
/// </summary>
/// <remarks>
/// <para>
/// A reference is <em>draft</em> use when it comes from a draft; <em>live</em> when it comes
/// from the version its publication points at; <em>history</em> otherwise. Liveness is read
/// from the publication at query time, so moving the live pointer never rewrites references.
/// </para>
/// <para>
/// Admin reads for a site named by the caller: every query is filtered to that site
/// explicitly (<see cref="PagesDbContext.ForSite{T}"/>), never by the request's resolved site.
/// </para>
/// </remarks>
internal sealed class MediaUsageReader(PagesDbContext database) : IMediaUsageReader
{
    public async Task<IReadOnlyDictionary<Guid, MediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);

        var result = mediaIds.Distinct().ToDictionary(id => id, _ => MediaUsageSummary.None);
        if (result.Count == 0)
        {
            return result;
        }

        var ids = result.Keys.ToList();
        var references = await ReferencesAsync(siteId, ids, cancellationToken).ConfigureAwait(false);

        foreach (var group in references.GroupBy(r => r.MediaId))
        {
            var places = Places(group);
            result[group.Key] = new MediaUsageSummary(
                places.Count,
                places.Count(p => p.InDraft),
                places.Count(p => p.Live),
                places.Count(p => p.InHistory && !p.InDraft && !p.Live));
        }

        return result;
    }

    public async Task<IReadOnlyList<MediaUsagePlace>> ListAsync(
        Guid siteId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var references = await ReferencesAsync(siteId, [mediaId], cancellationToken).ConfigureAwait(false);
        var places = Places(references);
        if (places.Count == 0)
        {
            return [];
        }

        var pageIds = places.Where(p => p.Kind == SubjectKinds.Page).Select(p => p.Id).ToList();
        var componentIds = places.Where(p => p.Kind == SubjectKinds.Component).Select(p => p.Id).ToList();

        var pageTitles = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Where(p => pageIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title })
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            .ConfigureAwait(false);

        var componentNames = await database.ForSite<Component>(siteId)
            .AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        // For live components: how many live pages show them (012's page → component edges).
        var liveComponentIds = places.Where(p => p.Kind == SubjectKinds.Component && p.Live).Select(p => p.Id).ToList();
        var livePagesByComponent = liveComponentIds.Count == 0
            ? []
            : await database.ForSite<LiveDependency>(siteId)
                .AsNoTracking()
                .Where(d => d.SubjectKind == SubjectKinds.Page
                    && d.TargetKind == DependencyKinds.Component
                    && liveComponentIds.Contains(d.TargetId))
                .GroupBy(d => d.TargetId)
                .Select(g => new { ComponentId = g.Key, Pages = g.Count() })
                .ToDictionaryAsync(g => g.ComponentId, g => g.Pages, cancellationToken)
                .ConfigureAwait(false);

        return
        [
            .. places
                .Select(p => p.Kind == SubjectKinds.Page
                    ? new MediaUsagePlace(p.Kind, p.Id, pageTitles.GetValueOrDefault(p.Id) ?? string.Empty, p.InDraft, p.Live, p.InHistory, null)
                    : new MediaUsagePlace(
                        p.Kind,
                        p.Id,
                        componentNames.GetValueOrDefault(p.Id) ?? string.Empty,
                        p.InDraft,
                        p.Live,
                        p.InHistory,
                        p.Live ? livePagesByComponent.GetValueOrDefault(p.Id) : null))
                .OrderBy(p => p.Kind == SubjectKinds.Page ? 0 : 1)
                .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id),
        ];
    }

    /// <summary>Every reference to the files, draft and published, with where it comes from.</summary>
    private async Task<List<Reference>> ReferencesAsync(Guid siteId, List<Guid> mediaIds, CancellationToken cancellationToken)
    {
        var drafts = await database.ForSite<DraftMediaReference>(siteId)
            .AsNoTracking()
            .Where(r => mediaIds.Contains(r.MediaId))
            .Select(r => new Reference(r.MediaId, r.SubjectKind, r.SubjectId, Source.Draft))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var versions = await (
                from reference in database.ForSite<VersionMediaReference>(siteId)
                join version in database.ForSite<PublishedVersion>(siteId) on reference.VersionId equals version.Id
                join publication in database.ForSite<Publication>(siteId)
                    on new { version.SubjectKind, version.SubjectId } equals new { publication.SubjectKind, publication.SubjectId }
                where mediaIds.Contains(reference.MediaId)
                select new Reference(
                    reference.MediaId,
                    version.SubjectKind,
                    version.SubjectId,
                    publication.LiveVersionId == version.Id ? Source.Live : Source.History))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        drafts.AddRange(versions);
        return drafts;
    }

    private static List<Place> Places(IEnumerable<Reference> references) =>
    [
        .. references
            .GroupBy(r => (r.Kind, r.SubjectId))
            .Select(g => new Place(
                g.Key.Kind,
                g.Key.SubjectId,
                g.Any(r => r.Source == Source.Draft),
                g.Any(r => r.Source == Source.Live),
                g.Any(r => r.Source == Source.History))),
    ];

    private enum Source
    {
        Draft,
        Live,
        History,
    }

    private sealed record Reference(Guid MediaId, string Kind, Guid SubjectId, Source Source);

    private sealed record Place(string Kind, Guid Id, bool InDraft, bool Live, bool InHistory);
}
