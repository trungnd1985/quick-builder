using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.Menus;

internal sealed record CreateMenuCommand(Guid SiteId, string Name, Guid? DuplicateOf) : ICommand<Result<MenuDetailResponse>>;

internal sealed class CreateMenuValidator : IValidator<CreateMenuCommand>
{
    public ValidationResult Validate(CreateMenuCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Menu.NormaliseName(instance.Name) is null
            ? ValidationResult.Invalid(new ValidationFailure("name", PagesErrors.InvalidMenuName.Message))
            : ValidationResult.Valid();
    }
}

/// <summary>
/// Creates a menu with an empty draft, or with a copy of another menu's draft; nothing is
/// published (024 FR-001, FR-002).
/// </summary>
internal sealed class CreateMenuHandler(MenuWrites writes, MenuReads reads, PagesAccess access)
    : ICommandHandler<CreateMenuCommand, Result<MenuDetailResponse>>
{
    public async Task<Result<MenuDetailResponse>> HandleAsync(CreateMenuCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuDetailResponse>(PagesErrors.SiteNotFound);
        }

        var created = await writes.CreateAsync(command.SiteId, command.Name, command.DuplicateOf, cancellationToken).ConfigureAwait(false);
        return created.IsFailure
            ? Result.Failure<MenuDetailResponse>(created.Error)
            : Result.Success((await reads.OneAsync(command.SiteId, created.Value, cancellationToken).ConfigureAwait(false))!);
    }
}

/// <summary>Administrative surface: create or duplicate a menu.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus")]
public sealed class CreateMenuController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.MenusManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, MenuNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new CreateMenuCommand(siteId, request.Name, request.DuplicateOf), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? PagesErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"/api/v1/pages/sites/{siteId}/menus/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
