using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Identity.Features.ReplaceGrants;

/// <summary>One site role in a request.</summary>
public sealed record SiteRoleRequest(Guid SiteId, string? Role);

/// <summary>A user's complete set of roles (contracts/identity-api.yaml, Grants).</summary>
public sealed record GrantsRequest(bool Administrator, IReadOnlyList<SiteRoleRequest>? SiteRoles);

internal sealed record ReplaceGrantsCommand(Guid UserId, GrantsRequest? Grants) : ICommand<Result<UserResponse>>;

internal sealed class ReplaceGrantsValidator : IValidator<ReplaceGrantsCommand>
{
    public ValidationResult Validate(ReplaceGrantsCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var failures = GrantsInput.ValidateShape(instance.Grants, "Grants").ToList();
        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Replaces a user's roles as a whole (spec FR-020, FR-013; research R-13).
/// </summary>
/// <remarks>
/// Always under the administrator lock, whether or not Administrator changes, so two
/// simultaneous replacements run one after the other and neither sees a half-written set.
/// The change applies on the user's next request: grants are read per request.
/// </remarks>
internal sealed class ReplaceGrantsHandler(
    IdentityDbContext database,
    AdministratorGuard guard,
    ISiteDirectory sites,
    IAccessEvaluator access,
    SecurityEventRecorder events,
    TimeProvider clock)
    : ICommandHandler<ReplaceGrantsCommand, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> HandleAsync(ReplaceGrantsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var grants = command.Grants!;

        if (!await database.Users.AnyAsync(u => u.Id == command.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<UserResponse>(IdentityErrors.UserNotFound);
        }

        await GrantsInput.EnsureSitesExistAsync(grants, sites, "Grants", cancellationToken).ConfigureAwait(false);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await guard.LockAsync(cancellationToken).ConfigureAwait(false);

        var before = await database.RoleGrants.AsNoTracking()
            .Where(g => g.UserId == command.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var losesAdministrator = before.Exists(g => g.IsAdministrator) && !grants.Administrator;
        if (losesAdministrator
            && !await guard.AnotherActiveAdministratorExistsAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            events.Record(SecurityEventTypes.GrantsChanged, SecurityEventOutcomes.Refused, access.UserId, command.UserId, new ReasonDetails("last_administrator"));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure<UserResponse>(IdentityErrors.LastAdministrator);
        }

        // Removed first and in a statement of its own: re-adding the same (user, site) in one
        // batch could otherwise trip the one-role-per-site index.
        await database.RoleGrants.Where(g => g.UserId == command.UserId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var after = GrantsInput.ToGrants(grants, command.UserId, access.UserId, clock.GetUtcNow()).ToList();
        database.RoleGrants.AddRange(after);

        events.Record(
            SecurityEventTypes.GrantsChanged,
            SecurityEventOutcomes.Succeeded,
            access.UserId,
            command.UserId,
            new GrantsChangedDetails(GrantsInput.Summarise(before), GrantsInput.Summarise(after)));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success((await UserReader.ReadAsync(database, command.UserId, cancellationToken).ConfigureAwait(false))!);
    }
}

[ApiController]
[Route("api/v1/identity/users/{userId:guid}/grants")]
public sealed class ReplaceGrantsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceAsync(Guid userId, GrantsRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ReplaceGrantsCommand(userId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
