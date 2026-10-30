using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>The read side of shared components (contracts/pages-api.yaml, Components).</summary>
internal sealed class ComponentReads(PagesDbContext database, PagesAccess access, PublicationReads publications)
{
    /// <summary>The site's components by name, each with how many page drafts use it.</summary>
    public async Task<ComponentListResponse> ListAsync(Guid siteId, CancellationToken cancellationToken) =>
        new(
            await ProjectAsync(siteId, database.ForSite<Component>(siteId), cancellationToken).ConfigureAwait(false),
            await access.PermissionsAsync(siteId, cancellationToken).ConfigureAwait(false));

    /// <summary>One component, or null when the site has no such component.</summary>
    public async Task<ComponentResponse?> OneAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken) =>
        (await ProjectAsync(siteId, database.ForSite<Component>(siteId).Where(c => c.Id == componentId), cancellationToken)
            .ConfigureAwait(false))
        .SingleOrDefault();

    private async Task<List<ComponentResponse>> ProjectAsync(
        Guid siteId,
        IQueryable<Component> components,
        CancellationToken cancellationToken)
    {
        var rows = await components
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Version,
                c.CreatedAtUtc,
                c.UpdatedAtUtc,
                c.CopiedFromId,
                c.CopiedFromSavedAtUtc,
                c.CopiedBy,
                c.CopiedAtUtc,
                UsageCount = database.ForSite<ComponentUsage>(siteId).Count(u => u.ComponentId == c.Id),
                Draft = database.ForSite<ComponentDraft>(siteId)
                    .Where(d => d.ComponentId == c.Id)
                    .Select(d => new { d.SchemaVersion, d.Version, d.UpdatedAtUtc, d.UpdatedBy })
                    .First(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await access.NamesAsync(rows.Select(r => r.Draft.UpdatedBy).Concat(rows.Where(r => r.CopiedBy != null).Select(r => r.CopiedBy!.Value)), cancellationToken).ConfigureAwait(false);

        // 012 FR-007: each component's publication state.
        var publication = await publications
            .SummariesAsync(siteId, SubjectKinds.Component, [.. rows.Select(r => r.Id)], cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(r => new ComponentResponse(
                r.Id,
                r.Name,
                r.UsageCount,
                r.Version,
                new DraftSummaryResponse(
                    r.Draft.SchemaVersion,
                    r.Draft.Version,
                    r.Draft.UpdatedAtUtc,
                    PagesAccess.Reference(r.Draft.UpdatedBy, names)),
                r.CreatedAtUtc,
                r.UpdatedAtUtc,
                publication[r.Id])
            {
                CopyOrigin = r is { CopiedFromId: { } source, CopiedFromSavedAtUtc: { } saved, CopiedBy: { } by, CopiedAtUtc: { } at }
                    ? new CopyOriginResponse(source, saved, PagesAccess.Reference(by, names), at)
                    : null,
            }),
        ];
    }
}
