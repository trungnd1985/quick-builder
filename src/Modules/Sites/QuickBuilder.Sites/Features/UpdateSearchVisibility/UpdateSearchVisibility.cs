using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.UpdateSearchVisibility;

/// <summary>
/// "Discourage search engines", the custom robots.txt rules and the host name redirect, and
/// the site version the caller last saw (017 FR-014, FR-025, FR-027, seo-api.yaml).
/// </summary>
public sealed record UpdateSearchVisibilityRequest(
    bool DiscourageSearchEngines,
    string? RobotsRules,
    bool RedirectOtherHostNames,
    uint Version);

internal sealed record UpdateSearchVisibilityCommand(
    Guid SiteId,
    bool DiscourageSearchEngines,
    string? RobotsRules,
    bool RedirectOtherHostNames,
    uint Version) : ICommand<Result<SiteResponse>>;

/// <summary>
/// Replaces whether and how search engines see the site (017 research SE-07, SE-16, SE-17).
/// Administrator only: a mistake here removes the site from search.
/// </summary>
internal sealed class UpdateSearchVisibilityHandler(SiteSettingsChange change, TimeProvider clock)
    : ICommandHandler<UpdateSearchVisibilityCommand, Result<SiteResponse>>
{
    public Task<Result<SiteResponse>> HandleAsync(UpdateSearchVisibilityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return change.ApplyAsync(
            command.SiteId,
            command.Version,
            site => site.ChangeSearchVisibility(
                command.DiscourageSearchEngines, command.RobotsRules, command.RedirectOtherHostNames, change.Actor, clock),
            cancellationToken);
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/search-visibility")]
public sealed class UpdateSearchVisibilityController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(SitesCapabilities.SearchVisibilityManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid siteId, UpdateSearchVisibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new UpdateSearchVisibilityCommand(
                siteId, request.DiscourageSearchEngines, request.RobotsRules, request.RedirectOtherHostNames, request.Version),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
