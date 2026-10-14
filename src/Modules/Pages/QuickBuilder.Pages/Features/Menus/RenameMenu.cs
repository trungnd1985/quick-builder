using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.Menus;

internal sealed record RenameMenuCommand(Guid SiteId, Guid MenuId, string Name, uint? Version) : ICommand<Result<MenuDetailResponse>>;

internal sealed class RenameMenuValidator : IValidator<RenameMenuCommand>
{
    public ValidationResult Validate(RenameMenuCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        if (Menu.NormaliseName(instance.Name) is null)
        {
            failures.Add(new ValidationFailure("name", PagesErrors.InvalidMenuName.Message));
        }

        if (instance.Version is null)
        {
            failures.Add(new ValidationFailure("version", "The menu version you saw is required."));
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid([.. failures]);
    }
}

/// <summary>
/// Renames a menu. The draft is untouched, so its unpublished-changes state does not move, and
/// visitors keep the published name until the next publish (024 research NV-05).
/// </summary>
internal sealed class RenameMenuHandler(MenuWrites writes, MenuReads reads, PagesAccess access)
    : ICommandHandler<RenameMenuCommand, Result<MenuDetailResponse>>
{
    public async Task<Result<MenuDetailResponse>> HandleAsync(RenameMenuCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuDetailResponse>(PagesErrors.SiteNotFound);
        }

        var renamed = await writes.RenameAsync(command.SiteId, command.MenuId, command.Name, command.Version!.Value, cancellationToken).ConfigureAwait(false);
        return renamed.IsFailure
            ? Result.Failure<MenuDetailResponse>(renamed.Error)
            : Result.Success((await reads.OneAsync(command.SiteId, command.MenuId, cancellationToken).ConfigureAwait(false))!);
    }
}

/// <summary>Administrative surface: rename a menu.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}")]
public sealed class RenameMenuController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(PagesCapabilities.MenusManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameAsync(Guid siteId, Guid menuId, MenuNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new RenameMenuCommand(siteId, menuId, request.Name, request.Version), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
