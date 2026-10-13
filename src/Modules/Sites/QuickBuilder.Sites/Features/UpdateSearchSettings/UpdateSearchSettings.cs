using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.UpdateSearchSettings;

/// <summary>
/// The title suffix, default sharing image and organisation, and the site version the caller
/// last saw (017 FR-006, seo-api.yaml).
/// </summary>
public sealed record UpdateSearchSettingsRequest(
    string? TitleSuffix,
    Guid? DefaultImageId,
    string? OrganisationName,
    Guid? OrganisationLogoId,
    uint Version);

internal sealed record UpdateSearchSettingsCommand(
    Guid SiteId,
    string? TitleSuffix,
    Guid? DefaultImageId,
    string? OrganisationName,
    Guid? OrganisationLogoId,
    uint Version) : ICommand<Result<SiteResponse>>;

/// <summary>
/// Replaces how the site presents itself to search engines and link previews (017 research
/// SE-08). The media ids are not checked here: Sites references no other module, and the
/// public media reader shows only the site's own files.
/// </summary>
internal sealed class UpdateSearchSettingsHandler(SiteSettingsChange change, TimeProvider clock)
    : ICommandHandler<UpdateSearchSettingsCommand, Result<SiteResponse>>
{
    public Task<Result<SiteResponse>> HandleAsync(UpdateSearchSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return change.ApplyAsync(
            command.SiteId,
            command.Version,
            site => site.ChangeSearchSettings(
                command.TitleSuffix, command.DefaultImageId, command.OrganisationName, command.OrganisationLogoId, change.Actor, clock),
            cancellationToken);
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/search-settings")]
public sealed class UpdateSearchSettingsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(SitesCapabilities.SearchManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid siteId, UpdateSearchSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new UpdateSearchSettingsCommand(
                siteId, request.TitleSuffix, request.DefaultImageId, request.OrganisationName, request.OrganisationLogoId, request.Version),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
