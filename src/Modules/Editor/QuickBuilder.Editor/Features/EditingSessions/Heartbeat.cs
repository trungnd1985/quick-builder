using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record HeartbeatCommand(Guid SiteId, Guid SessionId, bool Active) : ICommand<Result<PresenceResponse>>;

/// <summary>
/// Keeps a session alive and answers with what it should show (023 research CE-03, CE-05).
/// Every heartbeat checks the kind's capabilities again (FR-019): without the edit
/// capability a held lock is released; without the view capability the session ends.
/// </summary>
internal sealed class HeartbeatHandler(EditingSessionStore sessions, EditingAccess editing, TimeProvider clock)
    : ICommandHandler<HeartbeatCommand, Result<PresenceResponse>>
{
    public async Task<Result<PresenceResponse>> HandleAsync(HeartbeatCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.GetUtcNow();
        var session = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<PresenceResponse>(EditorErrors.SessionGone);
        }

        if (!await editing.MayViewAsync(session.SiteId, session.DocumentKind, cancellationToken).ConfigureAwait(false))
        {
            await sessions.DeleteAsync(session, cancellationToken).ConfigureAwait(false);
            return Result.Failure<PresenceResponse>(EditorErrors.EditingForbidden);
        }

        var mayEdit = await editing.MayEditAsync(session.SiteId, session.DocumentKind, cancellationToken).ConfigureAwait(false);
        if (!mayEdit && session.HoldsLock)
        {
            await sessions.ReleaseLockAsync(session, cancellationToken).ConfigureAwait(false);
        }

        await sessions.TouchAsync(session, command.Active && mayEdit, now, cancellationToken).ConfigureAwait(false);

        var current = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        return current is null
            ? Result.Failure<PresenceResponse>(EditorErrors.SessionGone)
            : Result.Success(await sessions.PresenceAsync(current, mayEdit, now, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: an editing session's heartbeat, every 10 seconds.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/heartbeat")]
public sealed class HeartbeatController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BeatAsync(Guid siteId, Guid sessionId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new HeartbeatCommand(siteId, sessionId, request.Active), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
