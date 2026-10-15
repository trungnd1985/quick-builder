using System.Text.Json;

namespace QuickBuilder.Rendering.Rendering.Navigation;

/// <summary>
/// The pages and items a live menu points at, so a render reads them in the same batches as
/// the page's other links (024 research NV-09).
/// </summary>
internal static class NavigationReferences
{
    public static void Collect(JsonElement document, ISet<Guid> pages, ISet<Guid> items)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(items);

        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty("entries", out var entries))
        {
            Walk(entries, pages, items);
        }
    }

    private static void Walk(JsonElement entries, ISet<Guid> pages, ISet<Guid> items)
    {
        if (entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (entry.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object
                && target.TryGetProperty("type", out var type))
            {
                if (type.GetString() == "page" && target.TryGetProperty("pageId", out var page) && Guid.TryParse(page.GetString(), out var pageId))
                {
                    pages.Add(pageId);
                }
                else if (type.GetString() == "item" && target.TryGetProperty("itemId", out var item) && Guid.TryParse(item.GetString(), out var itemId))
                {
                    items.Add(itemId);
                }
            }

            if (entry.TryGetProperty("children", out var children))
            {
                Walk(children, pages, items);
            }
        }
    }
}
