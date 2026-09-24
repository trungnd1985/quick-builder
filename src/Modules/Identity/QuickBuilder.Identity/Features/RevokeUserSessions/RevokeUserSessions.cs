using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.RevokeUserSessions;

internal sealed record RevokeUserSessionsCommand(Guid UserId) : ICommand<Result>;

/// <summary>
/// "Sign out everywhere" (spec FR-012a; US3 scenario 11): the answer to a lost laptop or a
/// suspected stolen session. The account stays active and can sign in again at once; its
/// known devices are forgotten, so a stolen device cookie is worthless too.
/// </summary>
internal sealed class RevokeUserSessionsHandler(
    IdentityDbContext database,
    IAccessEvaluator access,
    SessionService sessions,
    KnownDeviceService devices,
    SecurityEventRecorder events)
    : ICommandHandler<RevokeUserSessionsCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeUserSessionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await database.Users.AnyAsync(u => u.Id == command.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        await sessions.RevokeAllAsync(command.UserId, SessionRevocationReasons.SignOutEverywhere, exceptSessionId: null, cancellationToken).ConfigureAwait(false);
        await devices.RevokeAllAsync(command.UserId, access.UserId, "sign_out_everywhere", cancellationToken).ConfigureAwait(false);
        events.Record(
            SecurityEventTypes.SessionsRevoked,
            SecurityEventOutcomes.Succeeded,
            access.UserId,
            command.UserId,
            new SessionsRevokedDetails("everywhere", "administrator"));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

[ApiController]
[Route("api/v1/identity/users/{userId:guid}/sessions/revoke")]
public sealed class RevokeUserSessionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RevokeUserSessionsCommand(userId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error!) : NoContent();
    }
}
