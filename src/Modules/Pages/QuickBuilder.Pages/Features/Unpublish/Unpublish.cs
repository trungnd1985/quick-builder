using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Unpublish;

internal sealed record UnpublishCommand(Guid SiteId, string SubjectKind, Guid SubjectId, Guid? RedirectToPageId = null) : ICommand<Result<PublicationResponse>>;

/// <summary>017 FR-032: optionally, the page the unpublished page's address should redirect to.</summary>
public sealed record UnpublishPageRequest(Guid? RedirectToPageId = null);

/// <summary>
/// Takes a page or component off the public site; its draft, versions and history stay
/// (012 FR-004, FR-005).
/// </summary>
internal sealed class UnpublishHandler(PublishingWrites writes, AddressWarnings addresses) : ICommandHandler<UnpublishCommand, Result<PublicationResponse>>
{
    public async Task<Result<PublicationResponse>> HandleAsync(UnpublishCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await writes.UnpublishAsync(command.SiteId, command.SubjectKind, command.SubjectId, command.RedirectToPageId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure || command.SubjectKind != SubjectKinds.Page)
        {
            return result;
        }

        // 016 TB-12: a template taken off the site leaves its items without pages.
        var warnings = await addresses.AddressesLostAsync(command.SiteId, command.SubjectId, cancellationToken).ConfigureAwait(false);
        return Result.Success(result.Value with { Warnings = warnings });
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
    public async Task<IActionResult> UnpublishAsync(Guid siteId, Guid pageId, [FromBody] UnpublishPageRequest? request, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new UnpublishCommand(siteId, SubjectKinds.Page, pageId, request?.RedirectToPageId), cancellationToken)
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

/// <summary>Administrative surface: unpublish a menu; refused while published content shows it (024 FR-003).</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/unpublish")]
public sealed class UnpublishMenuController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnpublishAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new UnpublishCommand(siteId, SubjectKinds.Menu, menuId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
