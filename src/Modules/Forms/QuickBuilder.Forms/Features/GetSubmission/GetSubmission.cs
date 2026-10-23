using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.GetSubmission;

internal sealed record GetSubmissionCommand(Guid SiteId, Guid SubmissionId) : ICommand<Result<SubmissionResponse>>;

/// <summary>One submission with every value; opening it marks it read (018 FR-030).</summary>
internal sealed class GetSubmissionHandler(FormsDbContext database, SubmissionReads reads, FormsAccess access)
    : ICommandHandler<GetSubmissionCommand, Result<SubmissionResponse>>
{
    public async Task<Result<SubmissionResponse>> HandleAsync(GetSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await access.SiteAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<SubmissionResponse>(FormsErrors.SiteNotFound);
        }

        var submission = await database.ForSite<Submission>(command.SiteId)
            .FirstOrDefaultAsync(s => s.Id == command.SubmissionId, cancellationToken)
            .ConfigureAwait(false);
        if (submission is null)
        {
            return Result.Failure<SubmissionResponse>(FormsErrors.SubmissionNotFound);
        }

        if (!submission.IsRead)
        {
            submission.MarkRead();
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var error = await database.ForSite<NotificationDelivery>(command.SiteId)
            .Where(d => d.SubmissionId == submission.Id)
            .Select(d => d.LastError)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var forms = await reads.FormsAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        return Result.Success(SubmissionResponses.Detail(submission, forms, error));
    }
}

/// <summary>Administrative surface: one submission.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions/{submissionId:guid}")]
public sealed class GetSubmissionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid submissionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new GetSubmissionCommand(siteId, submissionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
