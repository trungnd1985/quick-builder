using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.ListSubmissions;

internal sealed record ListSubmissionsQuery(Guid SiteId, SubmissionQueryRequest Parameters) : IQuery<Result<SubmissionListResponse>>;

/// <summary>
/// A page of the site's submissions, newest first, filtered and searched (018 FR-025 to
/// FR-027; research FS-15). Spam is a box of its own (FR-016a).
/// </summary>
internal sealed class ListSubmissionsHandler(SubmissionReads reads, FormsAccess access)
    : IQueryHandler<ListSubmissionsQuery, Result<SubmissionListResponse>>
{
    public async Task<Result<SubmissionListResponse>> HandleAsync(ListSubmissionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await access.SiteAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<SubmissionListResponse>(FormsErrors.SiteNotFound);
        }

        var filter = query.Parameters.ToFilter();
        if (filter.IsFailure)
        {
            return Result.Failure<SubmissionListResponse>(filter.Error);
        }

        var rows = await SubmissionReads.Page(reads.Matching(query.SiteId, filter.Value), SubmissionReads.ParseCursor(query.Parameters.After))
            .AsNoTracking()
            .Take(SubmissionReads.PageSize + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var forms = await reads.FormsAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        var page = rows.Take(SubmissionReads.PageSize).ToList();
        return Result.Success(new SubmissionListResponse(
            [.. page.Select(s => SubmissionResponses.Summary(s, forms))],
            rows.Count > SubmissionReads.PageSize ? SubmissionReads.Cursor(page[^1]) : null,
            new SubmissionPermissionsResponse(await access.MayDeleteAsync(query.SiteId, cancellationToken).ConfigureAwait(false))));
    }
}

/// <summary>Administrative surface: list a site's submissions.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions")]
public sealed class ListSubmissionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, [FromQuery] SubmissionQueryRequest parameters, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListSubmissionsQuery(siteId, parameters), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
