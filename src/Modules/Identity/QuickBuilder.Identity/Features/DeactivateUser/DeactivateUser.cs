using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.DeactivateUser;

internal sealed record DeactivateUserCommand(Guid UserId) : ICommand<Result<UserResponse>>;

/// <summary>
/// Deactivates an account (spec FR-010; US3 scenario 6). Accounts are never deleted, so the
/// user's past actions stay attributed to them.
/// </summary>
/// <remarks>
/// Ends every session and known device and retires any live link in the same transaction,
/// so the user's very next request is refused (FR-017). Refused when the account is the
/// last active administrator (FR-013), under the administrator lock.
/// </remarks>
internal sealed class DeactivateUserHandler(
    IdentityDbContext database,
    AdministratorGuard guard,
    IAccessEvaluator access,
    SessionService sessions,
    KnownDeviceService devices,
    SetPasswordLinkService links,
    SecurityEventRecorder events,
    TimeProvider clock)
    : ICommandHandler<DeactivateUserCommand, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> HandleAsync(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await guard.LockAsync(cancellationToken).ConfigureAwait(false);

        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<UserResponse>(IdentityErrors.UserNotFound);
        }

        if (user.IsActive
            && await guard.IsAdministratorAsync(user.Id, cancellationToken).ConfigureAwait(false)
            && !await guard.AnotherActiveAdministratorExistsAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            events.Record(SecurityEventTypes.AccountDeactivated, SecurityEventOutcomes.Refused, access.UserId, user.Id, new ReasonDetails("last_administrator"));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure<UserResponse>(IdentityErrors.LastAdministrator);
        }

        user.Deactivate(clock.GetUtcNow());
        await sessions.RevokeAllAsync(user.Id, SessionRevocationReasons.Deactivated, exceptSessionId: null, cancellationToken).ConfigureAwait(false);
        await devices.RevokeAllAsync(user.Id, access.UserId, "deactivated", cancellationToken).ConfigureAwait(false);
        await links.InvalidateLiveAsync(user.Id, LinkInvalidationReasons.Deactivated, cancellationToken).ConfigureAwait(false);
        events.Record(SecurityEventTypes.AccountDeactivated, SecurityEventOutcomes.Succeeded, access.UserId, user.Id);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success((await UserReader.ReadAsync(database, user.Id, cancellationToken).ConfigureAwait(false))!);
    }
}

[ApiController]
[Route("api/v1/identity/users/{userId:guid}/deactivate")]
public sealed class DeactivateUserController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeactivateUserCommand(userId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
