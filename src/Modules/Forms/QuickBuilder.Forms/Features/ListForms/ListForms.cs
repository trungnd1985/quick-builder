using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.ListForms;

internal sealed record ListFormsQuery(Guid SiteId) : IQuery<Result<FormListResponse>>;

/// <summary>Every form ever published on the site, newest first, for filters and columns (018 FR-027).</summary>
internal sealed class ListFormsHandler(SubmissionReads reads, FormsAccess access) : IQueryHandler<ListFormsQuery, Result<FormListResponse>>
{
    public async Task<Result<FormListResponse>> HandleAsync(ListFormsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await access.SiteAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<FormListResponse>(FormsErrors.SiteNotFound);
        }

        var forms = await reads.FormsAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        return Result.Success(new FormListResponse(
            [.. forms.Values
                .OrderByDescending(f => f.Form.LastPublishedAtUtc)
                .Select(f => new FormSummaryResponse(
                    f.Form.FormId,
                    f.Form.Name,
                    f.Published,
                    f.Form.RetentionDays,
                    JsonSerializer.Deserialize<List<FormFieldResponse>>(f.Form.Fields, JsonSerializerOptions.Web) ?? [],
                    f.Form.LastPublishedAtUtc))]));
    }
}

/// <summary>Administrative surface: the site's forms.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/forms")]
public sealed class ListFormsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListFormsQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
