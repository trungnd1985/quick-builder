using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Features.ReplaceGrants;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Identity.Features.CreateUser;

public sealed record CreateUserRequest(string? DisplayName, string? Email, GrantsRequest? Grants);

/// <summary>The new account and its first set-password link, shown once.</summary>
public sealed record CreateUserResponse(UserResponse User, IssuedLinkResponse SetPasswordLink);

/// <summary>A link's token and expiry. The admin shell builds the URL (research R-10).</summary>
public sealed record IssuedLinkResponse(string Token, DateTimeOffset ExpiresAtUtc);

internal sealed record CreateUserCommand(string? DisplayName, string? Email, GrantsRequest? Grants) : ICommand<Result<CreateUserResponse>>;

internal sealed class CreateUserValidator : IValidator<CreateUserCommand>
{
    public ValidationResult Validate(CreateUserCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = GrantsInput.ValidateShape(instance.Grants, nameof(instance.Grants)).ToList();
        if (User.ValidateDisplayName(User.NormaliseDisplayName(instance.DisplayName)) is { } nameError)
        {
            failures.Add(new ValidationFailure(nameof(instance.DisplayName), nameError.Message));
        }

        if (User.ValidateEmail(User.NormaliseEmail(instance.Email)) is { } emailError)
        {
            failures.Add(new ValidationFailure(nameof(instance.Email), emailError.Message));
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Creates an account without a password, and its first set-password link (spec FR-010,
/// FR-011; US3 scenario 1).
/// </summary>
/// <remarks>
/// Email delivery is optional in this product, so the product sends nothing: the
/// administrator is shown the link once and passes it on (FR-011).
/// </remarks>
internal sealed class CreateUserHandler(
    IdentityDbContext database,
    ISiteDirectory sites,
    IAccessEvaluator access,
    SetPasswordLinkService links,
    SecurityEventRecorder events,
    TimeProvider clock)
    : ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>
{
    public async Task<Result<CreateUserResponse>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await GrantsInput.EnsureSitesExistAsync(command.Grants!, sites, nameof(command.Grants), cancellationToken).ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var user = User.Create(command.DisplayName!, command.Email!, now).Value;

        if (await database.Users.AnyAsync(u => u.Email == user.Email, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<CreateUserResponse>(IdentityErrors.EmailTaken);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var grants = GrantsInput.ToGrants(command.Grants!, user.Id, access.UserId, now).ToList();
        database.Users.Add(user);
        database.RoleGrants.AddRange(grants);

        try
        {
            // Saved before the link: the link references the user.
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (UniqueViolations.IsEmailTaken(ex))
        {
            // Lost a race with a simultaneous create for the same address.
            return Result.Failure<CreateUserResponse>(IdentityErrors.EmailTaken);
        }

        var link = await links.IssueAsync(user.Id, access.UserId, cancellationToken).ConfigureAwait(false);
        events.Record(
            SecurityEventTypes.AccountCreated,
            SecurityEventOutcomes.Succeeded,
            access.UserId,
            user.Id,
            new AccountCreatedDetails(user.Email, GrantsInput.Summarise(grants)));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var created = await UserReader.ReadAsync(database, user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(new CreateUserResponse(created!, new IssuedLinkResponse(link.Token, link.ExpiresAtUtc)));
    }
}

/// <summary>Recognises the email uniqueness constraint in a failed save.</summary>
internal static class UniqueViolations
{
    public static bool IsEmailTaken(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } postgres
        && postgres.ConstraintName == "ux_users_email";
}

[ApiController]
[Route("api/v1/identity/users")]
public sealed class CreateUserController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new CreateUserCommand(request.DisplayName, request.Email, request.Grants), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? IdentityProblems.ToActionResult(result.Error)
            : Created($"{Request.PathBase}/api/v1/identity/users/{result.Value.User.Id}", result.Value);
    }
}
