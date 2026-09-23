using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Features.GetMe;

/// <summary>One site role the user holds.</summary>
public sealed record SiteRoleResponse(Guid SiteId, string Role);

/// <summary>The signed-in user, their roles and effective access (contracts/identity-api.yaml, Me).</summary>
public sealed record MeResponse(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsAdministrator,
    IReadOnlyList<SiteRoleResponse> SiteRoles);

internal sealed record GetMeQuery : IQuery<Result<MeResponse>>;

internal sealed class GetMeHandler(IdentityDbContext database, IAccessEvaluator access)
    : IQueryHandler<GetMeQuery, Result<MeResponse>>
{
    public async Task<Result<MeResponse>> HandleAsync(GetMeQuery query, CancellationToken cancellationToken)
    {
        var me = access.UserId is { } userId
            ? await MeReader.ReadAsync(database, userId, cancellationToken).ConfigureAwait(false)
            : null;

        return me is null
            ? Result.Failure<MeResponse>(IdentityErrors.Unauthenticated)
            : Result.Success(me);
    }
}

/// <summary>Builds <see cref="MeResponse"/>; shared with setup and sign-in, which return it.</summary>
internal static class MeReader
{
    public static async Task<MeResponse?> ReadAsync(IdentityDbContext database, Guid userId, CancellationToken cancellationToken)
    {
        var user = await database.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return null;
        }

        var grants = await database.RoleGrants
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => new { g.Role, g.SiteId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MeResponse(
            user.Id,
            user.DisplayName,
            user.Email,
            grants.Exists(g => g.Role == SystemRole.Administrator),
            [.. grants
                .Where(g => g.SiteId.HasValue)
                .Select(g => new SiteRoleResponse(g.SiteId!.Value, g.Role.ToString().ToLowerInvariant()))]);
    }
}

/// <summary>Administrative surface: who am I, and what may I do?</summary>
[ApiController]
[Route("api/v1/identity/me")]
public sealed class GetMeController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetMeQuery(), cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? IdentityProblems.ToActionResult(result.Error) : Ok(result.Value);
    }
}
