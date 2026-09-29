using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Rollback;

/// <summary>The retained version to publish again.</summary>
public sealed record RollbackRequest(int VersionNumber);

internal sealed record RollbackCommand(Guid SiteId, string SubjectKind, Guid SubjectId, int VersionNumber)
    : ICommand<Result<PublicationResponse>>;

/// <summary>
/// Publishes a retained version again as a new version; the draft is not changed (012 FR-014).
/// </summary>
internal sealed class RollbackHandler(PublishingWrites writes) : ICommandHandler<RollbackCommand, Result<PublicationResponse>>
{
    public Task<Result<PublicationResponse>> HandleAsync(RollbackCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return writes.RollbackAsync(command.SiteId, command.SubjectKind, command.SubjectId, command.VersionNumber, cancellationToken);
    }
}

/// <summary>Administrative surface: roll a page back to a retained version.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/rollback")]
public sealed class RollbackPageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RollbackAsync(Guid siteId, Guid pageId, RollbackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new RollbackCommand(siteId, SubjectKinds.Page, pageId, request.VersionNumber), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: roll a shared component back to a retained version.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/rollback")]
public sealed class RollbackComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RollbackAsync(Guid siteId, Guid componentId, RollbackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new RollbackCommand(siteId, SubjectKinds.Component, componentId, request.VersionNumber), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
