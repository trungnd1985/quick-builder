using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.RetentionImpact;

internal sealed record RetentionImpactQuery(Guid SiteId, string FormId, int Days) : IQuery<Result<CountResponse>>;

/// <summary>
/// How many of a form's inbox submissions a retention period would delete now, so the editor
/// can warn before it is set (018 FR-023a, spec edge case).
/// </summary>
internal sealed class RetentionImpactHandler(FormsDbContext database, FormsAccess access, TimeProvider clock)
    : IQueryHandler<RetentionImpactQuery, Result<CountResponse>>
{
    public static IReadOnlySet<int> Periods { get; } = new HashSet<int> { 30, 90, 180, 365 };

    public async Task<Result<CountResponse>> HandleAsync(RetentionImpactQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await access.SiteAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<CountResponse>(FormsErrors.SiteNotFound);
        }

        if (!Periods.Contains(query.Days))
        {
            return Result.Failure<CountResponse>(FormsErrors.InvalidFilter("Days must be 30, 90, 180 or 365."));
        }

        var before = clock.GetUtcNow().AddDays(-query.Days);
        var count = await database.ForSite<Submission>(query.SiteId)
            .CountAsync(s => s.FormId == query.FormId && !s.IsSpam && s.ReceivedAtUtc < before, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(new CountResponse(count));
    }
}

/// <summary>Administrative surface: what a retention period would delete.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/forms/{formId}/retention-impact")]
public sealed class RetentionImpactController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, string formId, [FromQuery] int days, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new RetentionImpactQuery(siteId, formId, days), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
