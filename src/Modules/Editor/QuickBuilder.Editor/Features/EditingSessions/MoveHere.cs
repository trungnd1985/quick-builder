using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record MoveHereCommand(Guid SiteId, Guid SessionId, Guid HolderSessionId) : ICommand<Result<PresenceResponse>>;

/// <summary>
/// "Edit here instead" (023 FR-014, clarification Q4, research CE-06): moves the member's own
/// lock from another of their sessions to this one, active or stale. Not a takeover, so no
/// record is written; the former session learns it moved on its next heartbeat.
/// </summary>
internal sealed class MoveHereHandler(EditingSessionStore sessions, EditingAccess editing, TimeProvider clock)
    : ICommandHandler<MoveHereCommand, Result<PresenceResponse>>
{
    public async Task<Result<PresenceResponse>> HandleAsync(MoveHereCommand command, CancellationToken cancellationToken)
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

        // The other session left or lapsed meanwhile: there is nothing to move, so claim.
        var moved = (holder is null && await sessions.ClaimAsync(session, now, cancellationToken).ConfigureAwait(false))
            || EditingRules.CanMove(holder?.AsHolder(), command.HolderSessionId, session.UserId, now)
            && await sessions.TransferAsync(session, command.HolderSessionId, EditingSession.LostReasons.Moved, staleBefore: null, now, cancellationToken).ConfigureAwait(false);
        if (!moved)
        {
            var current = holder ?? await sessions.HolderAsync(session.SiteId, session.DocumentKind, session.DocumentId, now, cancellationToken).ConfigureAwait(false);
            return current is null
                ? Result.Failure<PresenceResponse>(EditorErrors.SessionGone)
                : Result.Failure<PresenceResponse>(await EditingRefusals.LockHeldAsync(sessions, current, now, cancellationToken).ConfigureAwait(false));
        }

        var mine = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        return mine is null
            ? Result.Failure<PresenceResponse>(EditorErrors.SessionGone)
            : Result.Success(await sessions.PresenceAsync(mine, mayEdit: true, now, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: move one's own lock to this session.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/move")]
public sealed class MoveHereController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveAsync(Guid siteId, Guid sessionId, HolderSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new MoveHereCommand(siteId, sessionId, request.HolderSessionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
