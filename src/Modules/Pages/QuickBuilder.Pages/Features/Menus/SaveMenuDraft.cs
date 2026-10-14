using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.Menus;

internal sealed record SaveMenuDraftCommand(Guid SiteId, Guid MenuId, SaveMenuDraftRequest Request) : ICommand<Result<MenuDetailResponse>>;

internal sealed class SaveMenuDraftValidator : IValidator<SaveMenuDraftCommand>
{
    public ValidationResult Validate(SaveMenuDraftCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return DraftWrites.CheckRequest(instance.Request.Document);
    }
}

/// <summary>
/// Replaces a menu's draft (024 FR-006 to FR-011): the menu contract's rules, then rule 8 for new
/// targets, then the version the member saw. Visitors see nothing until the menu is published.
/// </summary>
internal sealed class SaveMenuDraftHandler(MenuWrites writes, MenuReads reads, PagesAccess access)
    : ICommandHandler<SaveMenuDraftCommand, Result<MenuDetailResponse>>
{
    public async Task<Result<MenuDetailResponse>> HandleAsync(SaveMenuDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuDetailResponse>(PagesErrors.SiteNotFound);
        }

        var saved = await writes.SaveDraftAsync(command.SiteId, command.MenuId, command.Request.Document, command.Request.Version, cancellationToken)
            .ConfigureAwait(false);

        return saved.IsFailure
            ? Result.Failure<MenuDetailResponse>(saved.Error)
            : Result.Success((await reads.OneAsync(command.SiteId, command.MenuId, cancellationToken).ConfigureAwait(false))!);
    }
}

/// <summary>Administrative surface: save a menu's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/draft")]
public sealed class SaveMenuDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [DraftSizeLimit]
    [RequireCapability(PagesCapabilities.MenusManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, Guid menuId, SaveMenuDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new SaveMenuDraftCommand(siteId, menuId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
