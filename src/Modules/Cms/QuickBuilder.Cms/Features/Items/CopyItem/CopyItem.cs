using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.CopyItem;

/// <summary>"Save my version as a copy" for an item: what the form holds, and when the version it loaded was saved.</summary>
public sealed record CopyItemRequest(string? Slug, JsonElement? Values, DateTimeOffset BaseSavedAtUtc);

internal sealed record CopyItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, CopyItemRequest Request) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Saves the member's version of an item as a new, unpublished item of the same collection
/// (023 FR-020 to FR-022, research CE-11). The fields may have changed since the form was built:
/// values of fields that no longer exist, and values that can never be stored, are left out and
/// named in <see cref="ItemResponse.DroppedFields"/>; everything else is kept, as a draft keeps it.
/// </summary>
internal sealed class CopyItemHandler(ItemWrites writes, ItemReads reads, SlugAvailability slugs, CmsAccess access)
    : ICommandHandler<CopyItemCommand, Result<ItemResponse>>
{
    // A slug taken between choosing it and saving it is chosen again, a few times.
    private const int Attempts = 3;

    public async Task<Result<ItemResponse>> HandleAsync(CopyItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        if (ItemInput.ReadValues(command.Request.Values) is not { } entered)
        {
            return Result.Failure<ItemResponse>(CmsErrors.InvalidItem(ItemReads.Report(ItemInput.MalformedDocument)));
        }

        for (var attempt = 1; ; attempt++)
        {
            var dropped = new List<string>();
            var written = await writes.ExecuteAsync(
                    new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, null, null, access.Actor),
                    async context =>
                    {
                        var original = context.Item!;
                        var values = ItemValues.Read(JsonDocument.Parse(entered.Write().ToJsonString()));

                        // FR-022: only what the collection's fields can hold today.
                        foreach (var fieldId in values.FieldIds.ToList().Where(id => context.Schema.Fields.All(f => f.Id != id)))
                        {
                            values.Set(fieldId, null);
                            dropped.Add(fieldId);
                        }

                        var preferred = SlugRules.IsValid(command.Request.Slug?.Trim()) ? command.Request.Slug!.Trim() : original.Slug;
                        var slug = await slugs.FirstFreeAsync(
                                context.SiteId, context.CollectionId, SlugRules.CopyCandidates(preferred), null, cancellationToken)
                            .ConfigureAwait(false);

                        var report = await context.ValidateAsync(null, slug, values, cancellationToken).ConfigureAwait(false);
                        foreach (var fieldId in report.Problems.Where(p => ItemProblems.IsMalformed(p.Problem) && values.Has(p.FieldId)).Select(p => p.FieldId).Distinct().ToList())
                        {
                            values.Set(fieldId, null);
                            dropped.Add(fieldId);
                        }

                        var copy = original.CopyAsNew(slug, values, command.Request.BaseSavedAtUtc, context.Actor, context.Now);
                        context.Add(copy);
                        return Result.Success<Item?>(copy);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (written.IsFailure && written.Error.Code == "cms.item_slug_taken" && attempt < Attempts)
            {
                continue;
            }

            var answer = await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
            return answer.IsFailure ? answer : Result.Success(answer.Value with { DroppedFields = dropped });
        }
    }
}

/// <summary>Administrative surface: save a version of an item as a new item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/copies")]
public sealed class CopyItemController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [ItemSizeLimit]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyAsync(Guid siteId, Guid collectionId, Guid itemId, CopyItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new CopyItemCommand(siteId, collectionId, itemId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? CmsErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"/api/v1/cms/sites/{siteId}/collections/{collectionId}/items/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
