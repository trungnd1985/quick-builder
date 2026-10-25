using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record OpenSessionCommand(Guid SiteId, string Kind, Guid DocumentId, Guid? CollectionId) : ICommand<Result<PresenceResponse>>;

internal sealed class OpenSessionValidator : IValidator<OpenSessionCommand>
{
    public ValidationResult Validate(OpenSessionCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (!EditingDocumentKinds.IsKnown(instance.Kind))
        {
            return ValidationResult.Invalid(new ValidationFailure("kind", "The kind must be page, component or item."));
        }

        if (instance.DocumentId == Guid.Empty)
        {
            return ValidationResult.Invalid(new ValidationFailure("documentId", "A document id is required."));
        }

        var namesCollection = instance.CollectionId is { } collection && collection != Guid.Empty;
        return instance.Kind == EditingDocumentKinds.Item && !namesCollection
            ? ValidationResult.Invalid(new ValidationFailure("collectionId", "An item session names its collection."))
            : ValidationResult.Valid();
    }
}

/// <summary>
/// Opens an editing session as a viewer (023 FR-009, clarification Q3): nobody is blocked by
/// opening a document; the first change claims the lock. Answers with who else has it open.
/// </summary>
internal sealed class OpenSessionHandler(EditingSessionStore sessions, EditingAccess editing, EditorAccess access, TimeProvider clock)
    : ICommandHandler<OpenSessionCommand, Result<PresenceResponse>>
{
    public async Task<Result<PresenceResponse>> HandleAsync(OpenSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PresenceResponse>(EditorErrors.SiteNotFound);
        }

        if (!await editing.MayViewAsync(command.SiteId, command.Kind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PresenceResponse>(EditorErrors.EditingForbidden);
        }

        var now = clock.GetUtcNow();
        var session = EditingSession.Open(command.SiteId, command.Kind, command.DocumentId, command.CollectionId, editing.Actor, now);
        await sessions.AddAsync(session, cancellationToken).ConfigureAwait(false);

        var mayEdit = await editing.MayEditAsync(command.SiteId, command.Kind, cancellationToken).ConfigureAwait(false);
        return Result.Success(await sessions.PresenceAsync(session, mayEdit, now, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: open an editing session.</summary>
/// <remarks>
/// Every site role holds <see cref="PageContentCapabilities.View"/>, so another site's member
/// gets the usual 404. The handler then checks the document kind's own capability (CE-16).
/// </remarks>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing-sessions")]
public sealed class OpenSessionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OpenAsync(Guid siteId, OpenSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new OpenSessionCommand(siteId, request.Kind ?? string.Empty, request.DocumentId, request.CollectionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? EditorErrorResults.ToActionResult(result.Error)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }
}
