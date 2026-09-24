using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.RevokeOwnOtherSessions;

internal sealed record RevokeOwnOtherSessionsCommand : ICommand<Result>;

/// <summary>
/// "Sign out my other sessions" (spec FR-012a; US3 scenario 12). The session asking keeps
/// working; the user's known devices are kept, because this is the owner acting.
/// </summary>
internal sealed class RevokeOwnOtherSessionsHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    SessionService sessions,
    SecurityEventRecorder events)
    : ICommandHandler<RevokeOwnOtherSessionsCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeOwnOtherSessionsCommand command, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (IdentityClaims.GetUserId(principal) is not { } userId || IdentityClaims.GetSessionId(principal) is not { } sessionId)
        {
            return Result.Failure(IdentityErrors.Unauthenticated);
        }

        await sessions.RevokeAllAsync(userId, SessionRevocationReasons.SignOutOthers, sessionId, cancellationToken).ConfigureAwait(false);
        events.Record(SecurityEventTypes.SessionsRevoked, SecurityEventOutcomes.Succeeded, userId, userId, new SessionsRevokedDetails("others", "self"));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

[ApiController]
[Route("api/v1/identity/me/sessions/revoke-others")]
public sealed class RevokeOwnOtherSessionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RevokeOwnOtherSessionsCommand(), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error!) : NoContent();
    }
}
