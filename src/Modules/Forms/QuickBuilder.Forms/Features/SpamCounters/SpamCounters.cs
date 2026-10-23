using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.SpamCounters;

internal sealed record SpamCountersQuery(Guid SiteId) : IQuery<Result<SpamCountersResponse>>;

/// <summary>One form's spam protection over the last 30 days.</summary>
public sealed record SpamCounterResponse(string FormId, int TrappedKept, int TrappedDropped, int RateLimited);

public sealed record SpamCountersResponse(IReadOnlyList<SpamCounterResponse> Items);

/// <summary>
/// What spam protection held back per form over the last 30 days (018 FR-019): counts only, so
/// an editor can see the protection working, and notice when it holds back too much.
/// </summary>
internal sealed class SpamCountersHandler(FormsDbContext database, FormsAccess access, TimeProvider clock)
    : IQueryHandler<SpamCountersQuery, Result<SpamCountersResponse>>
{
    public const int Days = 30;

    public async Task<Result<SpamCountersResponse>> HandleAsync(SpamCountersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await access.SiteAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<SpamCountersResponse>(FormsErrors.SiteNotFound);
        }

        var since = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-(Days - 1));
        var items = await database.ForSite<SpamCounter>(query.SiteId)
            .Where(c => c.Day >= since)
            .GroupBy(c => c.FormId)
            .Select(g => new SpamCounterResponse(g.Key, g.Sum(c => c.TrappedKept), g.Sum(c => c.TrappedDropped), g.Sum(c => c.RateLimited)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new SpamCountersResponse([.. items.OrderBy(i => i.FormId, StringComparer.Ordinal)]));
    }
}

/// <summary>Administrative surface: the spam counters.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/spam-counters")]
public sealed class SpamCountersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new SpamCountersQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
