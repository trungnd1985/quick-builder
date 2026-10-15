using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Navigation;

/// <summary>Where an entry stands relative to the page being rendered (024 FR-027 to FR-029).</summary>
internal enum NavState
{
    None,

    /// <summary>The entry points at the page being rendered.</summary>
    Current,

    /// <summary>An ancestor of a current or section entry in the menu.</summary>
    Containing,

    /// <summary>No entry is current; this page entry is the nearest page above the current address.</summary>
    Section,
}

/// <summary>One menu entry as visitors get it: a label, perhaps a link, and its children (public-navigation.md §2).</summary>
internal sealed class ResolvedEntry
{
    public required string Id { get; init; }

    /// <summary>Content: written encoded, never logged.</summary>
    public required string Label { get; init; }

    /// <summary>Where the entry links, or null when it shows its label only.</summary>
    public string? Href { get; init; }

    /// <summary>Whether <see cref="Href"/> is an absolute external address rather than a site path or <c>#anchor</c>.</summary>
    public bool External { get; init; }

    public bool NewTab { get; init; }

    /// <summary>The page this entry points at, for active state; null for other kinds.</summary>
    public Guid? PageId { get; init; }

    /// <summary>The item this entry points at, for active state.</summary>
    public Guid? ItemId { get; init; }

    /// <summary>The page's address within the site (no base path), for the section rule; null for other kinds.</summary>
    public string? Path { get; init; }

    public List<ResolvedEntry> Children { get; init; } = [];

    public NavState State { get; set; }

    public bool OnCurrentPath => State != NavState.None;
}

/// <summary>
/// Turns a live menu document into the entries visitors see (024 research NV-10;
/// public-navigation.md §2).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><see cref="Resolve"/>: hidden entries go with their children; page and item targets
/// link to their current live address, or to nothing when they are not live; labels fall back to
/// the page's title or the item's name as visitors last saw them.</item>
/// <item><see cref="ActiveState.Mark"/> runs on that whole tree, so an entry beyond the shown
/// levels still marks its ancestors.</item>
/// <item><see cref="Prune"/>: levels beyond the block's, entries without a label, and entries that
/// neither link nor keep a shown child.</item>
/// </list>
/// Nothing here writes HTML, so the rules read as data in the tests.
/// </remarks>
internal static partial class MenuModel
{
    public static List<ResolvedEntry> Resolve(JsonElement document, RenderInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        return document.ValueKind == JsonValueKind.Object && document.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array
            ? ResolveAll(entries, inputs)
            : [];
    }

    /// <summary>Drops levels beyond <paramref name="levels"/>, then what has nothing to show.</summary>
    public static List<ResolvedEntry> Prune(IEnumerable<ResolvedEntry> entries, int levels, int level = 1)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var kept = new List<ResolvedEntry>();
        foreach (var entry in entries)
        {
            var children = level < levels ? Prune(entry.Children, levels, level + 1) : [];
            if (entry.Label.Length == 0 || (entry.Href is null && children.Count == 0))
            {
                continue;
            }

            entry.Children.Clear();
            entry.Children.AddRange(children);
            kept.Add(entry);
        }

        return kept;
    }

    private static List<ResolvedEntry> ResolveAll(JsonElement entries, RenderInputs inputs)
    {
        var resolved = new List<ResolvedEntry>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || (entry.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
                || !entry.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var children = entry.TryGetProperty("children", out var list) && list.ValueKind == JsonValueKind.Array ? ResolveAll(list, inputs) : [];
            var own = entry.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString()!.Trim() : string.Empty;
            var newTab = entry.TryGetProperty("newTab", out var tab) && tab.ValueKind == JsonValueKind.True;
            var id = entry.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;

            resolved.Add((target.TryGetProperty("type", out var type) ? type.GetString() : null) switch
            {
                "page" => Page(id, own, target, newTab, children, inputs),
                "item" => Item(id, own, target, newTab, children, inputs),
                "url" => Url(id, own, target, newTab, children),
                "anchor" => new ResolvedEntry { Id = id, Label = own, Href = Anchor(target), Children = children },
                _ => new ResolvedEntry { Id = id, Label = own, Children = children },
            });
        }

        return resolved;
    }

    private static ResolvedEntry Page(string id, string own, JsonElement target, bool newTab, List<ResolvedEntry> children, RenderInputs inputs)
    {
        if (!target.TryGetProperty("pageId", out var pageValue) || !Guid.TryParse(pageValue.GetString(), out var pageId)
            || !inputs.LinkTargets.TryGetValue(pageId, out var page))
        {
            return new ResolvedEntry { Id = id, Label = own, Children = children };
        }

        // FR-022, FR-023: the page's current address while it is live; its label as visitors last saw it.
        string? href = null;
        if (page.IsLive)
        {
            var path = PageShell.TrimBase(inputs.BasePath) + page.Path;
            var anchor = Anchor(target);
            href = HtmlWriter.IsSitePath(path) ? path + (anchor ?? string.Empty) : null;
        }

        return new ResolvedEntry
        {
            Id = id,
            Label = own.Length > 0 ? own : page.LiveTitle?.Trim() ?? string.Empty,
            Href = href,
            NewTab = newTab && href is not null,
            PageId = pageId,
            Path = page.IsLive ? page.Path : null,
            Children = children,
        };
    }

    private static ResolvedEntry Item(string id, string own, JsonElement target, bool newTab, List<ResolvedEntry> children, RenderInputs inputs)
    {
        if (!target.TryGetProperty("itemId", out var itemValue) || !Guid.TryParse(itemValue.GetString(), out var itemId)
            || inputs.MenuItems is not { } items || !items.TryGetValue(itemId, out var item))
        {
            return new ResolvedEntry { Id = id, Label = own, Children = children };
        }

        // 016 FR-004: an item's page is its collection's live template pattern with its slug.
        string? href = null;
        string? path = null;
        if (inputs.ItemPatterns is { } patterns && patterns.TryGetValue(item.CollectionId, out var pattern))
        {
            path = pattern.Replace(QuickBuilder.Pages.Contracts.TemplateInfo.SlugPlaceholder, item.Slug, StringComparison.Ordinal);
            var full = PageShell.TrimBase(inputs.BasePath) + path;
            href = HtmlWriter.IsSitePath(full) ? full : null;
        }

        return new ResolvedEntry
        {
            Id = id,
            Label = own.Length > 0 ? own : item.Name?.Trim() ?? string.Empty,
            Href = href,
            NewTab = newTab && href is not null,
            ItemId = itemId,
            Children = children,
        };
    }

    private static ResolvedEntry Url(string id, string own, JsonElement target, bool newTab, List<ResolvedEntry> children)
    {
        // The schemes a page link allows (page-schema §2a); HtmlWriter.Url checks again on writing.
        var href = target.TryGetProperty("href", out var value) ? value.GetString() : null;
        return new ResolvedEntry { Id = id, Label = own, Href = href, External = href is not null, NewTab = newTab && href is not null, Children = children };
    }

    /// <summary><c>#name</c>, or null when the target has no anchor or one outside the contract's pattern.</summary>
    private static string? Anchor(JsonElement target) =>
        target.TryGetProperty("anchor", out var anchor) && anchor.GetString() is { } name && AnchorName().IsMatch(name) ? "#" + name : null;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex AnchorName();
}
