using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Menus;

internal sealed record DeleteMenuCommand(Guid SiteId, Guid MenuId, uint Version) : ICommand<Result>;

/// <summary>
/// Deletes a menu (024 FR-003): refused while live pages or components show it. The admin first
/// lists the drafts that use it (<c>…/usages</c>) and asks for confirmation.
/// </summary>
internal sealed class DeleteMenuHandler(MenuWrites writes, PagesAccess access) : ICommandHandler<DeleteMenuCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteMenuCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(PagesErrors.SiteNotFound);
        }

        return await writes.DeleteAsync(command.SiteId, command.MenuId, command.Version, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: delete a menu.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}")]
public sealed class DeleteMenuController(IDispatcher dispatcher) : ControllerBase
{
    [HttpDelete]
    [RequireCapability(PagesCapabilities.MenusManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid menuId, [FromQuery] uint version, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteMenuCommand(siteId, menuId, version), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
