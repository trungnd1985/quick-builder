using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Features.SavePageDraft;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.SaveComponentDraft;

internal sealed record SaveComponentDraftCommand(Guid SiteId, Guid ComponentId, JsonElement Document, uint Version)
    : ICommand<Result<DraftResponse>>;

internal sealed class SaveComponentDraftValidator : IValidator<SaveComponentDraftCommand>
{
    public ValidationResult Validate(SaveComponentDraftCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return DraftWrites.CheckRequest(instance.Document);
    }
}

/// <summary>
/// Replaces a component's draft. The same rules as a page draft, plus: a component contains
/// no component (FR-027).
/// </summary>
internal sealed class SaveComponentDraftHandler(DraftWrites drafts, PagesAccess access)
    : ICommandHandler<SaveComponentDraftCommand, Result<DraftResponse>>
{
    public async Task<Result<DraftResponse>> HandleAsync(SaveComponentDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DraftResponse>(PagesErrors.SiteNotFound);
        }

        return await drafts
            .SaveComponentDraftAsync(command.SiteId, command.ComponentId, command.Document, command.Version, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: save a component's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/draft")]
public sealed class SaveComponentDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [DraftSizeLimit]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, Guid componentId, SaveDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new SaveComponentDraftCommand(siteId, componentId, request.Document, request.Version),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
