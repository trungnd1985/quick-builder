using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record ReleaseCommand(Guid SiteId, Guid SessionId) : ICommand<Result>;

/// <summary>
/// Leaving the document (023 FR-015): the session ends, and with it any lock it held. Sent on
/// route leave, on <c>pagehide</c> with a keepalive request, and before sign-out. Only the
/// caller's own session; nothing happens, and nothing is said, for any other id.
/// </summary>
internal sealed class ReleaseHandler(EditingSessionStore sessions, EditingAccess editing)
    : ICommandHandler<ReleaseCommand, Result>
{
    public async Task<Result> HandleAsync(ReleaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await sessions.DeleteOwnAsync(command.SiteId, command.SessionId, editing.Actor, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Administrative surface: end an editing session.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}")]
public sealed class ReleaseController(IDispatcher dispatcher) : ControllerBase
{
    [HttpDelete]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReleaseAsync(Guid siteId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ReleaseCommand(siteId, sessionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
