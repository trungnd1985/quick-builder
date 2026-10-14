using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Redirects;

internal sealed record ListRedirectsQuery(Guid SiteId, string? Search, string? State, string? Sort, string? Direction, int Page)
    : IQuery<Result<RedirectListResponse>>;

/// <summary>
/// A site's redirects, searched, filtered by state, sorted and paged 50 at a time (017 FR-015,
/// research SE-13).
/// </summary>
/// <remarks>
/// A site holds at most 10,000 redirects, so the list is filtered and sorted in memory after
/// one read, and a state filter sees every redirect. Whether a source is hidden by an item
/// page costs a Cms lookup, made only for sources under a live template.
/// </remarks>
internal sealed class ListRedirectsHandler(PagesDbContext database, RedirectWrites writes, RedirectViews views, PagesAccess access)
    : IQueryHandler<ListRedirectsQuery, Result<RedirectListResponse>>
{
    public const int PageSize = 50;

    public async Task<Result<RedirectListResponse>> HandleAsync(ListRedirectsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RedirectListResponse>(PagesErrors.SiteNotFound);
        }

        var state = await writes.LoadAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        var redirects = await database.ForSite<Redirect>(query.SiteId)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            redirects = [.. redirects.Where(r =>
                RedirectViews.Source(r).Contains(text, StringComparison.OrdinalIgnoreCase)
                || (RedirectViews.TargetAddress(state, r.Target) ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase)
                || (r.Note ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase))];
        }

        // Usage and states are computed for every remaining redirect only when sorting or
        // filtering needs them; otherwise for the page shown.
        var needsAll = query.State is not null || query.Sort is "hits" or "lastUsed";
        var all = needsAll ? await views.BuildAsync(state, redirects, cancellationToken).ConfigureAwait(false) : null;

        var rows = all is not null
            ? all.Where(r => query.State is null || r.State == query.State).ToList()
            : null;

        var descending = string.Equals(query.Direction, "desc", StringComparison.OrdinalIgnoreCase);
        var page = Math.Max(1, query.Page);

        if (rows is not null)
        {
            IEnumerable<RedirectResponse> sorted = query.Sort switch
            {
                "hits" => Order(rows, r => r.Hits, descending),
                "lastUsed" => Order(rows, r => r.LastUsedAt ?? DateTimeOffset.MinValue, descending),
                "updated" => Order(rows, r => r.UpdatedAt, descending),
                _ => descending ? rows.OrderByDescending(r => r.Source, StringComparer.Ordinal) : rows.OrderBy(r => r.Source, StringComparer.Ordinal),
            };

            return Result.Success(new RedirectListResponse([.. sorted.Skip((page - 1) * PageSize).Take(PageSize)], rows.Count, RedirectWrites.MaxPerSite));
        }

        IEnumerable<Redirect> ordered = query.Sort == "updated"
            ? Order(redirects, r => r.UpdatedAtUtc, descending)
            : descending ? redirects.OrderByDescending(RedirectViews.Source, StringComparer.Ordinal) : redirects.OrderBy(RedirectViews.Source, StringComparer.Ordinal);
        var shown = ordered.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        var items = await views.BuildAsync(state, shown, cancellationToken).ConfigureAwait(false);
        return Result.Success(new RedirectListResponse(items, redirects.Count, RedirectWrites.MaxPerSite));
    }

    private static IOrderedEnumerable<T> Order<T, TKey>(IEnumerable<T> rows, Func<T, TKey> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
}

/// <summary>Administrative surface: the site's redirects, a page at a time.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/redirects")]
public sealed class ListRedirectsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid siteId,
        [FromQuery] string? q,
        [FromQuery] string? state,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await dispatcher.QueryAsync(new ListRedirectsQuery(siteId, q, state, sort, dir, page), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
