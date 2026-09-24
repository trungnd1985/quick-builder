using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Features.GetUser;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;

namespace QuickBuilder.Identity.Features.ListUsers;

internal sealed record ListUsersQuery : IQuery<IReadOnlyList<UserResponse>>;

internal sealed class ListUsersHandler(IdentityDbContext database) : IQueryHandler<ListUsersQuery, IReadOnlyList<UserResponse>>
{
    public async Task<IReadOnlyList<UserResponse>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken) =>
        await UserReader.ReadManyAsync(database, filter: null, cancellationToken).ConfigureAwait(false);
}

[ApiController]
[Route("api/v1/identity/users")]
public sealed class ListUsersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Every account, ordered by name.</summary>
    [HttpGet]
    [RequireCapability(IdentityModule.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await dispatcher.QueryAsync(new ListUsersQuery(), cancellationToken).ConfigureAwait(false));
}
