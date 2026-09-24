using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Features.GetMe;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.GetUser;

/// <summary>A user's roles: installation-wide Administrator, and one role per site.</summary>
public sealed record GrantsResponse(bool Administrator, IReadOnlyList<SiteRoleResponse> SiteRoles);

/// <summary>An account as administrators see it (contracts/identity-api.yaml, User).</summary>
public sealed record UserResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string Status,
    bool HasPassword,
    GrantsResponse Grants,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSignInAtUtc);

internal sealed record GetUserQuery(Guid UserId) : IQuery<Result<UserResponse>>;

internal sealed class GetUserHandler(IdentityDbContext database) : IQueryHandler<GetUserQuery, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> HandleAsync(GetUserQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await UserReader.ReadAsync(database, query.UserId, cancellationToken).ConfigureAwait(false);
        return user is null ? Result.Failure<UserResponse>(IdentityErrors.UserNotFound) : Result.Success(user);
    }
}

/// <summary>Builds <see cref="UserResponse"/>s; shared by every user-management slice that returns one.</summary>
internal static class UserReader
{
    public static async Task<UserResponse?> ReadAsync(IdentityDbContext database, Guid userId, CancellationToken cancellationToken) =>
        (await ReadManyAsync(database, u => u.Id == userId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    public static async Task<List<UserResponse>> ReadManyAsync(
        IdentityDbContext database,
        System.Linq.Expressions.Expression<Func<User, bool>>? filter,
        CancellationToken cancellationToken)
    {
        var query = database.Users.AsNoTracking();
        if (filter is not null)
        {
            query = query.Where(filter);
        }

        var users = await query
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.Status,
                HasPassword = u.PasswordHash != null,
                u.CreatedAtUtc,
                u.LastSignInAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = users.ConvertAll(u => u.Id);
        var grants = await database.RoleGrants
            .AsNoTracking()
            .Where(g => ids.Contains(g.UserId))
            .Select(g => new { g.UserId, g.Role, g.SiteId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return users.ConvertAll(u =>
        {
            var own = grants.Where(g => g.UserId == u.Id).ToList();
            return new UserResponse(
                u.Id,
                u.DisplayName,
                u.Email,
                u.Status == UserStatus.Active ? "active" : "deactivated",
                u.HasPassword,
                new GrantsResponse(
                    own.Exists(g => g.Role == SystemRole.Administrator),
                    [.. own
                        .Where(g => g.SiteId.HasValue)
                        .Select(g => new SiteRoleResponse(g.SiteId!.Value, g.Role.ToString().ToLowerInvariant()))]),
                u.CreatedAtUtc,
                u.LastSignInAtUtc);
        });
    }
}

[ApiController]
[Route("api/v1/identity/users")]
public sealed class GetUserController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("{userId:guid}")]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetUserQuery(userId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
