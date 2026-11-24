using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Features.GetMe;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity.Features.CompleteSetup;

/// <summary>Request body for the setup wizard.</summary>
public sealed record CompleteSetupRequest(string? SetupCode, string? DisplayName, string? Email, string? Password);

internal sealed record CompleteSetupCommand(string? SetupCode, string? DisplayName, string? Email, string? Password)
    : ICommand<Result<MeResponse>>;

/// <summary>
/// Shape only. The setup code is deliberately not validated here: a missing code is
/// refused exactly like a wrong one, by the handler, so both are counted and recorded
/// (US1 scenario 5).
/// </summary>
internal sealed class CompleteSetupValidator : IValidator<CompleteSetupCommand>
{
    public ValidationResult Validate(CompleteSetupCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (User.ValidateDisplayName(User.NormaliseDisplayName(instance.DisplayName)) is { } nameError)
        {
            failures.Add(new ValidationFailure(nameof(instance.DisplayName), nameError.Message));
        }

        if (User.ValidateEmail(User.NormaliseEmail(instance.Email)) is { } emailError)
        {
            failures.Add(new ValidationFailure(nameof(instance.Email), emailError.Message));
        }

        failures.AddRange(PasswordPolicy.Check(instance.Password, instance.Email, instance.DisplayName, nameof(instance.Password)));

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Claims the installation and creates its first administrator, exactly once (spec FR-002
/// to FR-004; research R-12).
/// </summary>
/// <remarks>
/// Order matters: transport, then the client throttle, then the code — so a guessing
/// client is stopped before any comparison, and nothing expensive happens for it. The claim
/// is one conditional UPDATE in the same transaction as the account: concurrent
/// submissions all reach it, and the database lets exactly one through.
/// </remarks>
internal sealed class CompleteSetupHandler(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    TransportSecurity transport,
    SignInThrottleService throttle,
    InstallationStore installations,
    SetupStatus setupStatus,
    SecurityEventRecorder events,
    SessionService sessions,
    KnownDeviceService devices,
    Pbkdf2PasswordHasher hasher,
    TimeProvider clock)
    : ICommandHandler<CompleteSetupCommand, Result<MeResponse>>
{
    public async Task<Result<MeResponse>> HandleAsync(CompleteSetupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Setup runs only within an HTTP request.");

        if (transport.RequireSecureTransport(context) is { } insecure)
        {
            return Result.Failure<MeResponse>(insecure);
        }

        var clientKey = TransportSecurity.ClientKey(context.Connection.RemoteIpAddress);
        var clientCheck = await throttle.CheckClientAsync(clientKey, cancellationToken).ConfigureAwait(false);
        if (!clientCheck.Allowed)
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(clientCheck.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Result.Failure<MeResponse>(IdentityErrors.Throttled);
        }

        var installation = await installations.EnsureAsync(cancellationToken).ConfigureAwait(false);
        if (installation.IsClaimed || installation.SetupCode is null)
        {
            return Result.Failure<MeResponse>(IdentityErrors.SetupAlreadyCompleted);
        }

        var storedCode = installation.SetupCode;
        if (!Tokens.SetupCodesMatch(storedCode, Tokens.NormaliseSetupCode(command.SetupCode)))
        {
            await throttle.RecordFailureAsync(clientKey, accountKey: null, cancellationToken).ConfigureAwait(false);
            events.Record(SecurityEventTypes.SetupCodeRejected, SecurityEventOutcomes.Refused, actorUserId: null, subjectUserId: null);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure<MeResponse>(IdentityErrors.SetupCodeInvalid);
        }

        var now = clock.GetUtcNow();
        var user = User.Create(command.DisplayName!, command.Email!, now).Value;
        user.SetPasswordHash(await hasher.HashAsync(command.Password!, cancellationToken).ConfigureAwait(false), now);
        user.RecordSignIn(now);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var claimed = await database.Installation
            .Where(i => i.Id == Installation.SingletonId && i.ClaimedAtUtc == null && i.SetupCode == storedCode)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.ClaimedAtUtc, (DateTimeOffset?)now)
                    .SetProperty(i => i.SetupCode, (string?)null)
                    .SetProperty(i => i.SetupCodeCreatedAtUtc, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed != 1)
        {
            // Another submission won the race between our read and this update.
            return Result.Failure<MeResponse>(IdentityErrors.SetupAlreadyCompleted);
        }

        database.Users.Add(user);
        database.RoleGrants.Add(RoleGrant.Administrator(user.Id, grantedByUserId: null, now));
        sessions.Issue(context, user.Id);
        devices.Remember(context, user.Id, presented: null);
        events.Record(SecurityEventTypes.SetupCompleted, SecurityEventOutcomes.Succeeded, user.Id, user.Id);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        setupStatus.MarkClaimed();

        var me = await MeReader.ReadAsync(database, user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(me!);
    }
}

/// <summary>
/// Anonymous, and accepted only while unclaimed and only with the setup code
/// (contracts/authorization.md §4).
/// </summary>
[ApiController]
[Route("api/v1/identity/setup")]
public sealed class CompleteSetupController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetUpAsync(CompleteSetupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(
                new CompleteSetupCommand(request.SetupCode, request.DisplayName, request.Email, request.Password),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? IdentityProblems.ToActionResult(result.Error)
            : Created($"{Request.PathBase}/api/v1/identity/me", result.Value);
    }
}
