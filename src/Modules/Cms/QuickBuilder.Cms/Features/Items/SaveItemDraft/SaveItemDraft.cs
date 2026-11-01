using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.SaveItemDraft;

internal sealed record SaveItemDraftCommand(Guid SiteId, Guid CollectionId, Guid ItemId, DraftRequest Request, bool ClientKeepsEveryFormat, Guid? EditingSession = null)
    : ICommand<Result<ItemResponse>>;

/// <summary>
/// Saves an item's draft (015 US1, FR-005, FR-013, FR-014). Never touches its published values
/// (SC-004): only publishing does. Refused when the item or the schema changed since the form
/// was built, or when a value can never be stored.
/// </summary>
internal sealed partial class SaveItemDraftHandler(ItemWrites writes, ItemReads reads, CmsAccess access, IEditingLocks locks, ILogger<SaveItemDraftHandler> logger)
    : ICommandHandler<SaveItemDraftCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(SaveItemDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        var draft = ItemSlices.ReadDraft(command.Request);
        if (draft.IsFailure)
        {
            return Result.Failure<ItemResponse>(draft.Error);
        }

        // 023 research CE-07, FR-018: a session that does not hold the item's lock, while another
        // does, cannot save; no session or no live lock leaves it to the revision check (FR-026).
        var check = await locks.CheckWriteAsync(command.SiteId, EditingDocumentKinds.Item, command.ItemId, command.EditingSession, cancellationToken).ConfigureAwait(false);
        if (!check.Allowed && check.HolderUserId is { } holder)
        {
            var names = await access.NamesAsync([holder], cancellationToken).ConfigureAwait(false);
            LogLockHeld(logger, command.SiteId, command.ItemId, access.Actor, holder);
            return Result.Failure<ItemResponse>(CmsErrors.LockHeld(holder, names.GetValueOrDefault(holder) ?? string.Empty, check.HolderSince ?? DateTimeOffset.MinValue));
        }

        var (slug, values) = draft.Value;
        var written = await writes.ExecuteAsync(
                // 023 research CE-08: a form that states its draft revision is checked on that alone.
                new ItemWriteRequest(
                    command.SiteId,
                    command.CollectionId,
                    command.ItemId,
                    command.Request.DraftRevision is null ? command.Request.Revision : null,
                    command.Request.SchemaRevision,
                    access.Actor,
                    command.Request.DraftRevision),
                async context =>
                {
                    var item = context.Item!;

                    // 035 FR-020: an admin from before 035 would drop rich text formats it does not know.
                    if (!command.ClientKeepsEveryFormat && UsesExtendedFormats(context.Schema, item.DraftValues()))
                    {
                        return Result.Failure<Item?>(CmsErrors.AdminOutdated);
                    }

                    var report = await context.ValidateAsync(item.Id, slug, values, cancellationToken).ConfigureAwait(false);
                    if (ItemSlices.RefuseMalformed(report) is { } refused)
                    {
                        return Result.Failure<Item?>(refused);
                    }

                    item.SaveDraft(slug, values, context.Actor, context.Now);
                    context.Changed(item);
                    return Result.Success<Item?>(item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Item draft save refused, another session holds the lock: site {SiteId}, item {ItemId}, by {ActorId}; held by {HolderId}")]
    private static partial void LogLockHeld(ILogger logger, Guid siteId, Guid itemId, Guid actorId, Guid holderId);

    /// <summary>Whether any rich text value of the stored draft uses a 035 format (research RT-13).</summary>
    private static bool UsesExtendedFormats(CollectionSchemaDocument schema, ItemValues draft) =>
        schema.Fields
            .Where(f => f.Type == FieldType.RichText)
            .Select(f => draft.Get(f.Id))
            .OfType<JsonNode>()
            .Any(v => RichTextRules.UsesExtendedFormats(JsonSerializer.SerializeToElement(v)));
}

/// <summary>Administrative surface: save an item's draft.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}")]
public sealed class SaveItemDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ItemSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, Guid collectionId, Guid itemId, DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new SaveItemDraftCommand(
                siteId,
                collectionId,
                itemId,
                request,
                RichTextRules.KnowsEveryFormat(Request.Headers[RichTextRules.ClientHeader].FirstOrDefault()),
                EditingHeaders.Parse(Request.Headers[EditingHeaders.Session].FirstOrDefault())), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
