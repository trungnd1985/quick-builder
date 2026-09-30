using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Publish;

/// <summary>The draft version the caller last saw (012 research PR-03).</summary>
public sealed record PublishRequest(uint DraftVersion);

internal sealed record PublishCommand(Guid SiteId, string SubjectKind, Guid SubjectId, uint DraftVersion)
    : ICommand<Result<PublicationResponse>>;

/// <summary>
/// Publishes a page's or component's draft as its new live version (012 FR-001, FR-002).
/// </summary>
internal sealed class PublishHandler(PublishingWrites writes) : ICommandHandler<PublishCommand, Result<PublicationResponse>>
{
    public Task<Result<PublicationResponse>> HandleAsync(PublishCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return writes.PublishAsync(command.SiteId, command.SubjectKind, command.SubjectId, command.DraftVersion, cancellationToken);
    }
}

/// <summary>Administrative surface: publish a page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/publish")]
public sealed class PublishPageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishAsync(Guid siteId, Guid pageId, PublishRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new PublishCommand(siteId, SubjectKinds.Page, pageId, request.DraftVersion), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: publish a shared component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/publish")]
public sealed class PublishComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishAsync(Guid siteId, Guid componentId, PublishRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new PublishCommand(siteId, SubjectKinds.Component, componentId, request.DraftVersion), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
