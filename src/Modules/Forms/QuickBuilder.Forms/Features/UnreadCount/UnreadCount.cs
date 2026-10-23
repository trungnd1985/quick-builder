using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.UnreadCount;

internal sealed record UnreadCountQuery(Guid SiteId) : IQuery<Result<CountResponse>>;

/// <summary>How many inbox submissions are unread, for the admin navigation (018 FR-026); capped for display.</summary>
internal sealed class UnreadCountHandler(FormsDbContext database, FormsAccess access) : IQueryHandler<UnreadCountQuery, Result<CountResponse>>
{
    public const int Cap = 1000;

    public async Task<Result<CountResponse>> HandleAsync(UnreadCountQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await access.SiteAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<CountResponse>(FormsErrors.SiteNotFound);
        }

        var count = await database.ForSite<Submission>(query.SiteId)
            .Where(s => !s.IsSpam && !s.IsRead)
            .Take(Cap)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(new CountResponse(count));
    }
}

/// <summary>Administrative surface: the unread count.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions/unread-count")]
public sealed class UnreadCountController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new UnreadCountQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
