using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// One change to a site's own row, against the version the caller saw (009 research S-06),
/// shared by the settings slices that 017 adds.
/// </summary>
/// <remarks>
/// Saving moves the row's <c>xmin</c>, which every public cache key carries as the site's
/// version, so pages, the sitemap and robots.txt follow on every instance at their next
/// request (017 research SE-08). No event is needed.
/// </remarks>
internal sealed class SiteSettingsChange(
    SitesDbContext database,
    SiteDtoMapper mapper,
    IAccessEvaluator access,
    SiteDirectory directory)
{
    /// <summary>The signed-in user, who the change is recorded against (017 FR-021).</summary>
    public Guid Actor =>
        access.UserId ?? throw new InvalidOperationException("A settings change reached a handler without a signed-in user.");

    public async Task<Result<SiteResponse>> ApplyAsync(
        Guid siteId, uint version, Func<Site, Result> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Cross-site: the admin surface has no resolved site, and the caller named this one
        // explicitly in the path. Proven by SearchSettingsTests.
        var site = await database.Sites
            .AcrossAllSites("Admin operation on a site named explicitly in the request path (FR-011b).")
            .Include(s => s.HostNames)
            .FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return Result.Failure<SiteResponse>(Error.NotFound("sites.not_found", "No such site."));
        }

        if (site.Version != version)
        {
            return Result.Failure<SiteResponse>(ConcurrencyRetry.OutOfDate());
        }

        var changed = change(site);
        if (changed.IsFailure)
        {
            return Result.Failure<SiteResponse>(changed.Error!);
        }

        database.MarkAggregateChanged(site);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<SiteResponse>(ConcurrencyRetry.OutOfDate());
        }

        directory.Forget(site.Id);
        return Result.Success(await SiteResponses.BuildAsync(mapper.ToDto(site), access, cancellationToken).ConfigureAwait(false));
    }
}
