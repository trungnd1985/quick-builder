using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity.Features.ChangeOwnPassword;

public sealed record ChangeOwnPasswordRequest(string? CurrentPassword, string? NewPassword);

internal sealed record ChangeOwnPasswordCommand(string? CurrentPassword, string? NewPassword) : ICommand<Result>;

internal sealed class ChangeOwnPasswordValidator : IValidator<ChangeOwnPasswordCommand>
{
    public ValidationResult Validate(ChangeOwnPasswordCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return string.IsNullOrEmpty(instance.CurrentPassword)
            ? ValidationResult.Invalid(new ValidationFailure(nameof(instance.CurrentPassword), "Enter your current password."))
            : ValidationResult.Valid();
    }
}

/// <summary>
/// Changes the signed-in user's own password (spec FR-012; US3 scenario 8).
/// </summary>
/// <remarks>
/// The current password is required and checked under the per-account throttle, so a
/// stolen session cannot be used to guess it. The session making the change survives;
/// every other session of the user ends. Known devices are kept: this is the owner acting.
/// </remarks>
internal sealed class ChangeOwnPasswordHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    IAccessEvaluator access,
    SignInThrottleService throttle,
    SessionService sessions,
    SecurityEventRecorder events,
    Pbkdf2PasswordHasher hasher,
    TimeProvider clock)
    : ICommandHandler<ChangeOwnPasswordCommand, Result>
{
    public async Task<Result> HandleAsync(ChangeOwnPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Changing a password runs only within an HTTP request.");

        var user = access.UserId is { } userId
            ? await database.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false)
            : null;
        if (user?.PasswordHash is not { } currentHash)
        {
            return Result.Failure(IdentityErrors.Unauthenticated);
        }

        var clientKey = TransportSecurity.ClientKey(context.Connection.RemoteIpAddress);
        var accountKey = SignInThrottleService.AccountKey(user.Email);
        var accountCheck = await throttle.CheckAccountAsync(accountKey, cancellationToken).ConfigureAwait(false);
        if (!accountCheck.Allowed)
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(accountCheck.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Result.Failure(IdentityErrors.Throttled);
        }

        if (!Pbkdf2PasswordHasher.Verify(command.CurrentPassword!, currentHash))
        {
            await throttle.RecordFailureAsync(clientKey, accountKey, cancellationToken).ConfigureAwait(false);
            events.Record(SecurityEventTypes.PasswordChanged, SecurityEventOutcomes.Failed, user.Id, user.Id);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure(IdentityErrors.CurrentPasswordIncorrect);
        }

        var weakness = PasswordPolicy.Check(command.NewPassword, user.Email, user.DisplayName, nameof(command.NewPassword));
        if (weakness.Count > 0)
        {
            throw new RequestValidationException(ValidationResult.Invalid(weakness));
        }

        await throttle.RecordSuccessAsync(clientKey, accountKey, cancellationToken).ConfigureAwait(false);

        user.SetPasswordHash(hasher.Hash(command.NewPassword!), clock.GetUtcNow());
        await sessions.RevokeAllAsync(
            user.Id,
            SessionRevocationReasons.PasswordChanged,
            exceptSessionId: IdentityClaims.GetSessionId(context.User),
            cancellationToken).ConfigureAwait(false);
        events.Record(SecurityEventTypes.PasswordChanged, SecurityEventOutcomes.Succeeded, user.Id, user.Id);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

[ApiController]
[Route("api/v1/identity/me/password")]
public sealed class ChangeOwnPasswordController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangeAsync(ChangeOwnPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error!) : NoContent();
    }
}
