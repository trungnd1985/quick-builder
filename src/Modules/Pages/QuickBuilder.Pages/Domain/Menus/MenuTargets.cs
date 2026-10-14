using System.Text.Json;

namespace QuickBuilder.Pages.Domain.Menus;

/// <summary>A page or item a menu entry points at, with where it is in the document.</summary>
/// <param name="Kind"><see cref="MenuTargetKinds.Page"/> or <see cref="MenuTargetKinds.Item"/>.</param>
/// <param name="CollectionId">For an item, the collection the entry names; null for a page.</param>
/// <param name="Pointer">The JSON Pointer of the entry's <c>target</c>, where rule 8 reports.</param>
internal sealed record MenuTargetRef(string Kind, Guid Id, Guid? CollectionId, string Pointer);

/// <summary>
/// The pages and items a menu document points at (024 research NV-04, NV-07).
/// </summary>
/// <remarks>
/// Pure, over a document that has passed the menu schema, in document order. Used by the draft
/// save (rule 8 and the <c>menu_targets</c> index), and at publish for the menu's dependencies.
/// </remarks>
internal static class MenuTargets
{
    /// <summary>Every page and item target, in document order, duplicates included.</summary>
    public static IReadOnlyList<MenuTargetRef> Extract(string document)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        using var parsed = JsonDocument.Parse(document);
        var found = new List<MenuTargetRef>();
        if (!parsed.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (var (entry, pointer, _) in MenuRules.Walk(entries))
        {
            if (!entry.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (MenuRules.TargetType(entry))
            {
                case "page" when Guid.TryParse(target.GetProperty("pageId").GetString(), out var pageId):
                    found.Add(new MenuTargetRef(MenuTargetKinds.Page, pageId, null, $"{pointer}/target"));
                    break;
                case "item" when Guid.TryParse(target.GetProperty("itemId").GetString(), out var itemId)
                                 && Guid.TryParse(target.GetProperty("collectionId").GetString(), out var collectionId):
                    found.Add(new MenuTargetRef(MenuTargetKinds.Item, itemId, collectionId, $"{pointer}/target"));
                    break;
            }
        }

        return found;
    }

    /// <summary>The distinct (kind, id) pairs of <paramref name="targets"/>.</summary>
    public static IReadOnlySet<(string Kind, Guid Id)> Distinct(IEnumerable<MenuTargetRef> targets) =>
        targets.Select(t => (t.Kind, t.Id)).ToHashSet();

    /// <summary>How many entries a menu document holds, at every level.</summary>
    public static int CountEntries(string document)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        using var parsed = JsonDocument.Parse(document);
        return parsed.RootElement.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array
            ? MenuRules.Walk(entries).Count()
            : 0;
    }
}
