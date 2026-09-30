using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Unpublish;

internal sealed record UnpublishCommand(Guid SiteId, string SubjectKind, Guid SubjectId) : ICommand<Result<PublicationResponse>>;

/// <summary>
/// Takes a page or component off the public site; its draft, versions and history stay
/// (012 FR-004, FR-005).
/// </summary>
internal sealed class UnpublishHandler(PublishingWrites writes) : ICommandHandler<UnpublishCommand, Result<PublicationResponse>>
{
    public Task<Result<PublicationResponse>> HandleAsync(UnpublishCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return writes.UnpublishAsync(command.SiteId, command.SubjectKind, command.SubjectId, cancellationToken);
    }
}

/// <summary>Administrative surface: unpublish a page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/unpublish")]
public sealed class UnpublishPageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnpublishAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new UnpublishCommand(siteId, SubjectKinds.Page, pageId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: unpublish a shared component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/unpublish")]
public sealed class UnpublishComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnpublishAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new UnpublishCommand(siteId, SubjectKinds.Component, componentId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
