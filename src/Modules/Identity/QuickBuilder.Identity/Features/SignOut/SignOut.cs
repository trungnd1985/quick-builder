using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.SignOut;

internal sealed record SignOutCommand : ICommand<Result>;

/// <summary>
/// Ends the current session everywhere it could be used (US2 scenario 4): the row is
/// revoked, so even a replayed copy of the cookie stops working on every instance.
/// </summary>
/// <remarks>
/// The known-device cookie is left alone: signing out does not make this browser a
/// stranger to the account (research R-07).
/// </remarks>
internal sealed class SignOutHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    SessionService sessions,
    SecurityEventRecorder events)
    : ICommandHandler<SignOutCommand, Result>
{
    public async Task<Result> HandleAsync(SignOutCommand command, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Sign-out runs only within an HTTP request.");

        var userId = IdentityClaims.GetUserId(context.User);
        if (IdentityClaims.GetSessionId(context.User) is not { } sessionId || userId is null)
        {
            return Result.Failure(IdentityErrors.Unauthenticated);
        }

        await sessions.RevokeAsync(sessionId, SessionRevocationReasons.SignOut, cancellationToken).ConfigureAwait(false);
        events.Record(SecurityEventTypes.SignOut, SecurityEventOutcomes.Succeeded, userId, userId);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        SessionService.ClearCookie(context);
        return Result.Success();
    }
}

[ApiController]
[Route("api/v1/identity/sign-out")]
public sealed class SignOutController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SignOutAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new SignOutCommand(), cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error!) : NoContent();
    }
}
