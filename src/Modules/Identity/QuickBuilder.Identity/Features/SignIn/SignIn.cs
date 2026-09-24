using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Features.GetMe;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity.Features.SignIn;

/// <summary>Request body for signing in.</summary>
public sealed record SignInRequest(string? Email, string? Password);

internal sealed record SignInCommand(string? Email, string? Password) : ICommand<Result<MeResponse>>;

/// <summary>Presence only: whether the pair matches is the handler's question, answered once.</summary>
internal sealed class SignInValidator : IValidator<SignInCommand>
{
    public ValidationResult Validate(SignInCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(instance.Email))
        {
            failures.Add(new ValidationFailure(nameof(instance.Email), "Enter your email address."));
        }

        if (string.IsNullOrEmpty(instance.Password))
        {
            failures.Add(new ValidationFailure(nameof(instance.Password), "Enter your password."));
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Signs a user in (spec FR-014, FR-015, FR-015a; research R-07, R-08).
/// </summary>
/// <remarks>
/// The order is the design:
/// <list type="number">
/// <item>HTTPS or this machine (R-04).</item>
/// <item>The client block — before any lookup, so a blocked client costs almost nothing.</item>
/// <item>Find the account and any known device this browser holds for it.</item>
/// <item>The account wait, skipped for a known device, so strangers timing failures
/// cannot keep the owner out (FR-015a).</item>
/// <item>Verify — against a dummy hash when the account is missing or cannot sign in, so
/// every failure costs the same and looks the same (R-08).</item>
/// </list>
/// Throttled attempts are refused before the password is ever hashed.
/// </remarks>
internal sealed class SignInHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    TransportSecurity transport,
    SignInThrottleService throttle,
    KnownDeviceService devices,
    SessionService sessions,
    SecurityEventRecorder events,
    Pbkdf2PasswordHasher hasher,
    TimeProvider clock)
    : ICommandHandler<SignInCommand, Result<MeResponse>>
{
    public async Task<Result<MeResponse>> HandleAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Sign-in runs only within an HTTP request.");

        if (transport.RequireSecureTransport(context) is { } insecure)
        {
            return Result.Failure<MeResponse>(insecure);
        }

        var clientKey = TransportSecurity.ClientKey(context.Connection.RemoteIpAddress);
        var clientCheck = await throttle.CheckClientAsync(clientKey, cancellationToken).ConfigureAwait(false);
        if (!clientCheck.Allowed)
        {
            return await RefuseAsync(context, clientCheck, subjectUserId: null, "client_blocked", cancellationToken).ConfigureAwait(false);
        }

        var email = User.NormaliseEmail(command.Email);
        var accountKey = SignInThrottleService.AccountKey(email);

        var user = await database.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken)
            .ConfigureAwait(false);

        var device = user is null
            ? null
            : await devices.FindValidAsync(context, user.Id, cancellationToken).ConfigureAwait(false);

        if (device is null)
        {
            var accountCheck = await throttle.CheckAccountAsync(accountKey, cancellationToken).ConfigureAwait(false);
            if (!accountCheck.Allowed)
            {
                return await RefuseAsync(context, accountCheck, user?.Id, "account_delay", cancellationToken).ConfigureAwait(false);
            }
        }

        var verified = user is { CanSignIn: true, PasswordHash: { } hash }
            ? await Pbkdf2PasswordHasher.VerifyAsync(command.Password!, hash, cancellationToken).ConfigureAwait(false)
            : await hasher.VerifyAgainstDummyAsync(command.Password!, cancellationToken).ConfigureAwait(false);

        if (!verified)
        {
            await throttle.RecordFailureAsync(clientKey, accountKey, cancellationToken).ConfigureAwait(false);
            if (device is not null)
            {
                devices.RecordFailure(device);
            }

            // The attempted address is never stored; the account is named only when one
            // exists (FR-029).
            events.Record(
                SecurityEventTypes.SignIn,
                SecurityEventOutcomes.Failed,
                actorUserId: null,
                subjectUserId: user?.Id,
                new SignInDetails("invalid_credentials", KnownDevice: null));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<MeResponse>(IdentityErrors.InvalidCredentials);
        }

        var now = clock.GetUtcNow();
        await throttle.RecordSuccessAsync(clientKey, accountKey, cancellationToken).ConfigureAwait(false);

        if (hasher.NeedsRehash(user!.PasswordHash!))
        {
            // FR-009: stored hashes follow the configured cost as users sign in.
            user.SetPasswordHash(await hasher.HashAsync(command.Password!, cancellationToken).ConfigureAwait(false), now);
        }

        user.RecordSignIn(now);
        sessions.Issue(context, user.Id);
        devices.Remember(context, user.Id, device);
        events.Record(
            SecurityEventTypes.SignIn,
            SecurityEventOutcomes.Succeeded,
            user.Id,
            user.Id,
            new SignInDetails(Reason: null, KnownDevice: device is not null ? true : null));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var me = await MeReader.ReadAsync(database, user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(me!);
    }

    private async Task<Result<MeResponse>> RefuseAsync(
        HttpContext context,
        ThrottleDecision decision,
        Guid? subjectUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        events.Record(
            SecurityEventTypes.SignIn,
            SecurityEventOutcomes.Refused,
            actorUserId: null,
            subjectUserId,
            new SignInDetails(reason, KnownDevice: null));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Failure<MeResponse>(IdentityErrors.Throttled);
    }
}

/// <summary>Anonymous by necessity (contracts/authorization.md §4).</summary>
[ApiController]
[Route("api/v1/identity/sign-in")]
public sealed class SignInController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new SignInCommand(request.Email, request.Password), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
