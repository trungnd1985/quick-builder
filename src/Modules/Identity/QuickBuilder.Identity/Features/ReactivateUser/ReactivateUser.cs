using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.ReactivateUser;

internal sealed record ReactivateUserCommand(Guid UserId) : ICommand<Result<UserResponse>>;

/// <summary>
/// Reactivates an account: its existing password works again; its old sessions do not
/// (data-model.md, users).
/// </summary>
internal sealed class ReactivateUserHandler(
    IdentityDbContext database,
    IAccessEvaluator access,
    SecurityEventRecorder events,
    TimeProvider clock)
    : ICommandHandler<ReactivateUserCommand, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> HandleAsync(ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<UserResponse>(IdentityErrors.UserNotFound);
        }

        if (!user.IsActive)
        {
            user.Reactivate(clock.GetUtcNow());
            events.Record(SecurityEventTypes.AccountReactivated, SecurityEventOutcomes.Succeeded, access.UserId, user.Id);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success((await UserReader.ReadAsync(database, user.Id, cancellationToken).ConfigureAwait(false))!);
    }
}

[ApiController]
[Route("api/v1/identity/users/{userId:guid}/reactivate")]
public sealed class ReactivateUserController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ReactivateUserCommand(userId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
