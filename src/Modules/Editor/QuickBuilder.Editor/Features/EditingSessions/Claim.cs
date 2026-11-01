using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record ClaimCommand(Guid SiteId, Guid SessionId) : ICommand<Result<PresenceResponse>>;

/// <summary>
/// Takes the lock on the first change (023 FR-009, FR-010, research CE-04). Succeeds when no
/// live session holds it; refused naming the holder otherwise. Idempotent for the holder.
/// </summary>
internal sealed class ClaimHandler(EditingSessionStore sessions, EditingAccess editing, TimeProvider clock)
    : ICommandHandler<ClaimCommand, Result<PresenceResponse>>
{
    public async Task<Result<PresenceResponse>> HandleAsync(ClaimCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.GetUtcNow();
        var session = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<PresenceResponse>(EditorErrors.SessionGone);
        }

        if (!await editing.MayEditAsync(session.SiteId, session.DocumentKind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PresenceResponse>(EditorErrors.EditingForbidden);
        }

        var holder = await sessions.HolderAsync(session.SiteId, session.DocumentKind, session.DocumentId, now, cancellationToken).ConfigureAwait(false);
        if (holder is not null && holder.Id != session.Id)
        {
            return Result.Failure<PresenceResponse>(await EditingRefusals.LockHeldAsync(sessions, holder, now, cancellationToken).ConfigureAwait(false));
        }

        if (!await sessions.ClaimAsync(session, now, cancellationToken).ConfigureAwait(false))
        {
            // Someone else claimed it a moment ago.
            var winner = await sessions.HolderAsync(session.SiteId, session.DocumentKind, session.DocumentId, now, cancellationToken).ConfigureAwait(false);
            if (winner is not null)
            {
                return Result.Failure<PresenceResponse>(await EditingRefusals.LockHeldAsync(sessions, winner, now, cancellationToken).ConfigureAwait(false));
            }
        }

        var current = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        return current is null
            ? Result.Failure<PresenceResponse>(EditorErrors.SessionGone)
            : Result.Success(await sessions.PresenceAsync(current, mayEdit: true, now, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>The refusals the editing slices share.</summary>
internal static class EditingRefusals
{
    public static async Task<Error> LockHeldAsync(EditingSessionStore sessions, EditingSession holder, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (id, name, since, stale) = await sessions.DescribeAsync(holder, now, cancellationToken).ConfigureAwait(false);
        return EditorErrors.LockHeld(id, name, since, stale);
    }
}

/// <summary>Administrative surface: take a document's editing lock.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/claim")]
public sealed class ClaimController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClaimAsync(Guid siteId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ClaimCommand(siteId, sessionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
