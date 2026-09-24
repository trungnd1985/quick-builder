using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Features.CreateUser;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity.Features.UpdateUser;

/// <summary>Either or both; a field left out is left alone.</summary>
public sealed record UpdateUserRequest(string? DisplayName, string? Email);

internal sealed record UpdateUserCommand(Guid UserId, string? DisplayName, string? Email) : ICommand<Result<UserResponse>>;

internal sealed class UpdateUserValidator : IValidator<UpdateUserCommand>
{
    public ValidationResult Validate(UpdateUserCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        if (instance.DisplayName is null && instance.Email is null)
        {
            failures.Add(new ValidationFailure(string.Empty, "Change the name, the email address, or both."));
        }

        if (instance.DisplayName is not null && User.ValidateDisplayName(User.NormaliseDisplayName(instance.DisplayName)) is { } nameError)
        {
            failures.Add(new ValidationFailure(nameof(instance.DisplayName), nameError.Message));
        }

        if (instance.Email is not null && User.ValidateEmail(User.NormaliseEmail(instance.Email)) is { } emailError)
        {
            failures.Add(new ValidationFailure(nameof(instance.Email), emailError.Message));
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Renames a user and/or changes their email address (spec FR-010, FR-010a).
/// </summary>
/// <remarks>
/// An email change is a change of sign-in identity, so it ends every session and known
/// device and retires any live set-password link — a link sent to the old address must
/// not keep working. A change of letter case only is not an identity change: the address
/// already matched case-insensitively.
/// <para>
/// Simultaneous edits are last-write-wins (spec edge case); there is no concurrency token.
/// </para>
/// </remarks>
internal sealed class UpdateUserHandler(
    IdentityDbContext database,
    IAccessEvaluator access,
    SessionService sessions,
    KnownDeviceService devices,
    SetPasswordLinkService links,
    SecurityEventRecorder events,
    TimeProvider clock)
    : ICommandHandler<UpdateUserCommand, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<UserResponse>(IdentityErrors.UserNotFound);
        }

        var now = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (command.DisplayName is not null && User.NormaliseDisplayName(command.DisplayName) != user.DisplayName)
        {
            user.Rename(command.DisplayName, now);
            events.Record(SecurityEventTypes.DisplayNameChanged, SecurityEventOutcomes.Succeeded, access.UserId, user.Id);
        }

        if (command.Email is not null)
        {
            var oldEmail = user.Email;
            var newEmail = User.NormaliseEmail(command.Email);
            var identityChanges = !string.Equals(oldEmail, newEmail, StringComparison.OrdinalIgnoreCase);

            if (identityChanges
                && await database.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<UserResponse>(IdentityErrors.EmailTaken);
            }

            user.ChangeEmail(newEmail, now);

            if (identityChanges)
            {
                await sessions.RevokeAllAsync(user.Id, SessionRevocationReasons.EmailChanged, exceptSessionId: null, cancellationToken).ConfigureAwait(false);
                await devices.RevokeAllAsync(user.Id, access.UserId, "email_changed", cancellationToken).ConfigureAwait(false);
                await links.InvalidateLiveAsync(user.Id, LinkInvalidationReasons.EmailChanged, cancellationToken).ConfigureAwait(false);
                events.Record(
                    SecurityEventTypes.EmailChanged,
                    SecurityEventOutcomes.Succeeded,
                    access.UserId,
                    user.Id,
                    new EmailChangedDetails(oldEmail, newEmail));
            }
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (UniqueViolations.IsEmailTaken(ex))
        {
            return Result.Failure<UserResponse>(IdentityErrors.EmailTaken);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success((await UserReader.ReadAsync(database, user.Id, cancellationToken).ConfigureAwait(false))!);
    }
}

[ApiController]
[Route("api/v1/identity/users")]
public sealed class UpdateUserController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPatch("{userId:guid}")]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new UpdateUserCommand(userId, request.DisplayName, request.Email), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
