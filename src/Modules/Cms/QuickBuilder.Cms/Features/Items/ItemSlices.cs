using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items;

/// <summary>What several item slices share: reading a draft from a request and answering with an item.</summary>
internal static class ItemSlices
{
    /// <summary>A draft as a request sends it: its slug and values document, or the refusal for a malformed document.</summary>
    public static Result<(string Slug, ItemValues Values)> ReadDraft(DraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = ItemInput.ReadValues(request.Values);
        if (values is null)
        {
            return Result.Failure<(string, ItemValues)>(CmsErrors.InvalidItem(ItemReads.Report(ItemInput.MalformedDocument)));
        }

        return Result.Success((request.Slug?.Trim() ?? string.Empty, values));
    }

    /// <summary>The refusal of a draft with values that can never be stored (FR-013), or null.</summary>
    public static Error? RefuseMalformed(ItemReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.HasMalformed ? CmsErrors.InvalidItem(ItemReads.Report(report)) : null;
    }

    /// <summary>The written item as it now is, read after the commit.</summary>
    public static async Task<Result<ItemResponse>> AnswerAsync(
        Result<Item?> written,
        ItemReads reads,
        IReadOnlyList<ItemRefResponse>? unpublishedReferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(reads);

        if (written.IsFailure)
        {
            return Result.Failure<ItemResponse>(written.Error);
        }

        var item = written.Value ?? throw new InvalidOperationException("This operation answers with an item.");
        var response = await reads.GetAsync(item.SiteId, item.CollectionId, item.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("An item just written was not found.");
        return Result.Success(unpublishedReferences is null ? response : response with { UnpublishedReferences = unpublishedReferences });
    }
}
