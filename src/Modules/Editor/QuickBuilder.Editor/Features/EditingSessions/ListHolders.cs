using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record ListHoldersQuery(Guid SiteId, string Kind, Guid? CollectionId) : IQuery<Result<HoldersResponse>>;

internal sealed class ListHoldersValidator : IValidator<ListHoldersQuery>
{
    public ValidationResult Validate(ListHoldersQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return EditingDocumentKinds.IsKnown(instance.Kind)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(new ValidationFailure("kind", "The kind must be page, component or item."));
    }
}

/// <summary>
/// The documents of one kind on a site that someone is editing, for markers in the page tree,
/// the component list and an item list (023 FR-012, research CE-15).
/// </summary>
internal sealed class ListHoldersHandler(EditingSessionStore sessions, EditingAccess editing, EditorAccess access, TimeProvider clock)
    : IQueryHandler<ListHoldersQuery, Result<HoldersResponse>>
{
    public async Task<Result<HoldersResponse>> HandleAsync(ListHoldersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<HoldersResponse>(EditorErrors.SiteNotFound);
        }

        if (!await editing.MayViewAsync(query.SiteId, query.Kind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<HoldersResponse>(EditorErrors.EditingForbidden);
        }

        return Result.Success(await sessions.HolderListAsync(query.SiteId, query.Kind, query.CollectionId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: who is editing what, for list markers.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing")]
public sealed class ListHoldersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(Guid siteId, [FromQuery] string? kind, [FromQuery] Guid? collectionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListHoldersQuery(siteId, kind ?? string.Empty, collectionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
