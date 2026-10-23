using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.RestoreSubmission;

internal sealed record RestoreSubmissionCommand(Guid SiteId, Guid SubmissionId) : ICommand<Result<SubmissionResponse>>;

/// <summary>
/// Moves a submission the trap caught back to the inbox, unread, and notifies as if it had just
/// arrived (018 FR-016a, clarification 5). Recorded in the submission log.
/// </summary>
internal sealed class RestoreSubmissionHandler(
    FormsDbContext database, SubmissionReads reads, SubmissionLog log, FormsAccess access, IEmailSender email, TimeProvider clock)
    : ICommandHandler<RestoreSubmissionCommand, Result<SubmissionResponse>>
{
    public async Task<Result<SubmissionResponse>> HandleAsync(RestoreSubmissionCommand command, CancellationToken cancellationToken)
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

        var recipients = await database.ForSite<FormRecord>(command.SiteId)
            .Where(f => f.FormId == submission.FormId)
            .Select(f => f.Recipients.Length)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!submission.Restore(NotificationStates.Initial(hasRecipients: recipients > 0, emailConfigured: email.IsConfigured)))
        {
            return Result.Failure<SubmissionResponse>(FormsErrors.NotSpam);
        }

        if (submission.NotificationState == NotificationStates.Pending)
        {
            database.NotificationDeliveries.Add(NotificationDelivery.For(submission, clock.GetUtcNow()));
        }

        log.Add(command.SiteId, access.Actor, SubmissionLogActions.Restore, submission.FormId, count: 1);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var forms = await reads.FormsAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        return Result.Success(SubmissionResponses.Detail(submission, forms, notificationError: null));
    }
}

/// <summary>Administrative surface: restore from Spam.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions/{submissionId:guid}/restore")]
public sealed class RestoreSubmissionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, Guid submissionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RestoreSubmissionCommand(siteId, submissionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
