using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.MarkSubmission;

internal sealed record MarkSubmissionCommand(Guid SiteId, Guid SubmissionId, bool Read) : ICommand<Result>;

/// <summary>Marks a submission read or unread (018 FR-025).</summary>
internal sealed class MarkSubmissionHandler(FormsDbContext database, FormsAccess access) : ICommandHandler<MarkSubmissionCommand, Result>
{
    public async Task<Result> HandleAsync(MarkSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await access.SiteAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure(FormsErrors.SiteNotFound);
        }

        var submission = await database.ForSite<Submission>(command.SiteId)
            .FirstOrDefaultAsync(s => s.Id == command.SubmissionId, cancellationToken)
            .ConfigureAwait(false);
        if (submission is null)
        {
            return Result.Failure(FormsErrors.SubmissionNotFound);
        }

        if (command.Read)
        {
            submission.MarkRead();
        }
        else
        {
            submission.MarkUnread();
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>The body of <c>PUT …/read</c>.</summary>
public sealed record MarkSubmissionRequest(bool Read);

/// <summary>Administrative surface: mark a submission read or unread.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions/{submissionId:guid}/read")]
public sealed class MarkSubmissionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(FormsCapabilities.SubmissionsView)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsync(Guid siteId, Guid submissionId, MarkSubmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new MarkSubmissionCommand(siteId, submissionId, request.Read), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
