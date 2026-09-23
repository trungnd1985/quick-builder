using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;

namespace QuickBuilder.Identity.Features.GetStatus;

/// <summary>Whether the installation still needs the setup wizard.</summary>
public sealed record StatusResponse(bool SetupRequired);

internal sealed record GetStatusQuery : IQuery<StatusResponse>;

internal sealed class GetStatusHandler(IIdentitySetupStatus setupStatus) : IQueryHandler<GetStatusQuery, StatusResponse>
{
    public async Task<StatusResponse> HandleAsync(GetStatusQuery query, CancellationToken cancellationToken) =>
        new(!await setupStatus.IsClaimedAsync(cancellationToken).ConfigureAwait(false));
}

/// <summary>
/// Anonymous: the admin shell must know whether to show setup or sign-in before anyone is
/// signed in (contracts/authorization.md §4).
/// </summary>
[ApiController]
[Route("api/v1/identity/status")]
public sealed class GetStatusController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        Ok(await dispatcher.QueryAsync(new GetStatusQuery(), cancellationToken).ConfigureAwait(false));
}
