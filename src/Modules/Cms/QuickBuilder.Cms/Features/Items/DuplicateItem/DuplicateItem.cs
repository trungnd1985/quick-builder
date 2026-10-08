using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.DuplicateItem;

internal sealed record DuplicateItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Creates a draft copy of an item (015 US5 scenario 5, FR-007): the same draft values, the name
/// marked as a copy, a free slug derived from the original's, nothing published, no versions or
/// schedules.
/// </summary>
internal sealed class DuplicateItemHandler(ItemWrites writes, ItemReads reads, SlugAvailability slugs, CmsAccess access)
    : ICommandHandler<DuplicateItemCommand, Result<ItemResponse>>
{
    private const string CopyMark = " (copy)";

    // A slug taken between choosing it and saving it is chosen again, a few times.
    private const int Attempts = 3;

    public async Task<Result<ItemResponse>> HandleAsync(DuplicateItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        for (var attempt = 1; ; attempt++)
        {
            var written = await writes.ExecuteAsync(
                    new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, null, null, access.Actor),
                    async context =>
                    {
                        var original = context.Item!;
                        var slug = await slugs.FirstFreeAsync(
                                context.SiteId, context.CollectionId, SlugRules.CopyCandidates(original.Slug), null, cancellationToken)
                            .ConfigureAwait(false);

                        var copy = context.Item!.Duplicate(slug, CopyValues(original, context.Schema), context.Actor, context.Now);
                        context.Add(copy);
                        return Result.Success<Item?>(copy);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (written.IsFailure && written.Error.Code == "cms.item_slug_taken" && attempt < Attempts)
            {
                continue;
            }

            return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The original's draft values, its name marked as a copy and shortened to fit the title field.</summary>
    private static ItemValues CopyValues(Item original, CollectionSchemaDocument schema)
    {
        var values = original.DraftValues();
        if (schema.TitleField is { } title && values.Get(title.Id) is JsonValue value && value.TryGetValue<string>(out var name))
        {
            var room = Math.Min(title.MaxLength, CollectionSchemaLimits.MaxTextLength) - CopyMark.Length;
            var runes = name.EnumerateRunes().ToList();
            var kept = runes.Count <= room ? name : string.Concat(runes.Take(Math.Max(room, 0)).Select(r => r.ToString()));
            values.Set(title.Id, JsonValue.Create(kept + CopyMark));
        }

        return values;
    }
}

/// <summary>Administrative surface: duplicate an item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/duplicate")]
public sealed class DuplicateItemController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DuplicateAsync(Guid siteId, Guid collectionId, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DuplicateItemCommand(siteId, collectionId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? CmsErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"/api/v1/cms/sites/{siteId}/collections/{collectionId}/items/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
