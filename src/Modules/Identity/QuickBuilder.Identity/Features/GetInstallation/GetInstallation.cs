using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Versioning;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;

namespace QuickBuilder.Identity.Features.GetInstallation;

/// <summary>The running release and the database's (019 contracts/installation-version.md §4).</summary>
public sealed record InstallationResponse(
    string Version,
    string? Revision,
    string? DatabaseVersion,
    string? UpgradingTo,
    string? ReleaseNotesUrl);

internal sealed record GetInstallationQuery : IQuery<InstallationResponse>;

/// <summary>
/// What this installation runs (019 FR-006). Administrators only: version numbers help an
/// attacker, so nothing anonymous shows them (constitution Principle IX).
/// </summary>
internal sealed class GetInstallationHandler(IInstallationVersion installation) : IQueryHandler<GetInstallationQuery, InstallationResponse>
{
    /// <summary>A fixed link pattern; the server never calls out (constitution Principle II).</summary>
    internal const string ReleasesUrl = "https://github.com/trungnd1985/quick-builder/releases/tag/v";

    public async Task<InstallationResponse> HandleAsync(GetInstallationQuery query, CancellationToken cancellationToken)
    {
        var info = await installation.GetAsync(cancellationToken).ConfigureAwait(false);
        return new InstallationResponse(
            info.Version.ToString(),
            info.Revision,
            info.DatabaseVersion,
            info.UpgradingTo,
            info.Version.PreRelease == "dev" ? null : ReleasesUrl + info.Version);
    }
}

/// <summary>Administrative surface: the installation's release.</summary>
[ApiController]
[Route("api/v1/identity/installation")]
public sealed class GetInstallationController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(IdentityModule.ViewInstallation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        Ok(await dispatcher.QueryAsync(new GetInstallationQuery(), cancellationToken).ConfigureAwait(false));
}
