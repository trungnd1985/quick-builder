using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.SavePageDraft;

/// <summary>The whole new document, and the draft <see cref="Version"/> the caller last saw.</summary>
public sealed record SaveDraftRequest(JsonElement Document, uint Version);

internal sealed record SavePageDraftCommand(Guid SiteId, Guid PageId, JsonElement Document, uint Version, bool ClientKeepsEveryFormat, Guid? EditingSession = null)
    : ICommand<Result<DraftResponse>>;

internal sealed class SavePageDraftValidator : IValidator<SavePageDraftCommand>
{
    public ValidationResult Validate(SavePageDraftCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return DraftWrites.CheckRequest(instance.Document);
    }
}

/// <summary>
/// Replaces a page's draft with a document that follows the page schema contract (FR-018,
/// FR-021). Changes nothing visitors see, and publishes nothing (Principle VII).
/// </summary>
internal sealed class SavePageDraftHandler(DraftWrites drafts, PagesAccess access)
    : ICommandHandler<SavePageDraftCommand, Result<DraftResponse>>
{
    public async Task<Result<DraftResponse>> HandleAsync(SavePageDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DraftResponse>(PagesErrors.SiteNotFound);
        }

        return await drafts.SavePageDraftAsync(command.SiteId, command.PageId, command.Document, command.Version, command.ClientKeepsEveryFormat, command.EditingSession, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: save a page's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/draft")]
public sealed class SavePageDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [DraftSizeLimit]
    [RequireCapability(PagesCapabilities.PageDraftsEdit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, Guid pageId, SaveDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new SavePageDraftCommand(
                siteId,
                pageId,
                request.Document,
                request.Version,
                RichTextRules.KnowsEveryFormat(Request.Headers[RichTextRules.ClientHeader].FirstOrDefault()),
                EditingHeaders.Parse(Request.Headers[EditingHeaders.Session].FirstOrDefault())),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
