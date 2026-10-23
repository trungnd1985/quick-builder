using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features.DeleteSubmissions;

internal sealed record DeleteSubmissionsCommand(Guid SiteId, IReadOnlyList<Guid> Ids) : ICommand<Result<DeletedResponse>>;

/// <summary>
/// Deletes submissions for good, from the inbox or Spam (018 FR-028, FR-016a; clarification 1:
/// Designers and Administrators only). Ids of another site, or of nothing, are ignored; one log
/// row records who and how many (FR-030).
/// </summary>
internal sealed class DeleteSubmissionsHandler(FormsDbContext database, FormsAccess access, SubmissionLog log)
    : ICommandHandler<DeleteSubmissionsCommand, Result<DeletedResponse>>
{
    public const int MaxIds = 500;

    public async Task<Result<DeletedResponse>> HandleAsync(DeleteSubmissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await access.SiteAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<DeletedResponse>(FormsErrors.SiteNotFound);
        }

        if (command.Ids.Count is 0 or > MaxIds)
        {
            return Result.Failure<DeletedResponse>(FormsErrors.InvalidFilter($"Delete between 1 and {MaxIds} submissions at a time."));
        }

        var ids = command.Ids.Distinct().ToList();
        var submissions = await database.ForSite<Submission>(command.SiteId)
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (submissions.Count > 0)
        {
            database.Submissions.RemoveRange(submissions);
            log.Add(command.SiteId, access.Actor, SubmissionLogActions.Delete, formId: null, submissions.Count);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(new DeletedResponse(submissions.Count));
    }
}

/// <summary>The body of <c>DELETE …/submissions</c>.</summary>
public sealed record DeleteSubmissionsRequest(IReadOnlyList<Guid> Ids);

/// <summary>Administrative surface: delete submissions.</summary>
[ApiController]
[Route("api/v1/forms/sites/{siteId:guid}/submissions")]
public sealed class DeleteSubmissionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpDelete]
    [RequireCapability(FormsCapabilities.SubmissionsDelete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, DeleteSubmissionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new DeleteSubmissionsCommand(siteId, request.Ids ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? FormsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
