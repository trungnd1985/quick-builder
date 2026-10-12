using QuickBuilder.Cms.Contracts.Publishing;

namespace QuickBuilder.Rendering.Binding;

/// <summary>
/// The binding scopes around the node being rendered (016 FR-028, binding-expression.md §3):
/// the page's item on a template page, then the item of each enclosing list's card, innermost
/// last.
/// </summary>
internal sealed class ScopeStack
{
    public const string Page = "page";

    private readonly List<(string Name, PublishedItem Item)> _lists = [];

    public ScopeStack(PublishedItem? pageItem) => PageItem = pageItem;

    public PublishedItem? PageItem { get; }

    /// <summary>Whether a list's card is being rendered.</summary>
    public bool InList => _lists.Count > 0;

    /// <summary>Opens the scope of one card of the list <paramref name="listId"/>.</summary>
    public void Push(string listId, PublishedItem item) => _lists.Add((listId, item));

    public void Pop() => _lists.RemoveAt(_lists.Count - 1);

    /// <summary>
    /// The item a reader means: <paramref name="name"/>'s, or the nearest scope's when it names
    /// none. Null when that scope does not exist here, or has no item: a broken binding.
    /// </summary>
    public PublishedItem? Find(string? name)
    {
        if (name is null)
        {
            return _lists.Count > 0 ? _lists[^1].Item : PageItem;
        }

        if (name == Page)
        {
            return PageItem;
        }

        for (var i = _lists.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_lists[i].Name, name, StringComparison.Ordinal))
            {
                return _lists[i].Item;
            }
        }

        return null;
    }

    /// <summary>The items of every scope, outermost first: the page's item, then each card's.</summary>
    public IEnumerable<Guid> ItemIds()
    {
        if (PageItem is not null)
        {
            yield return PageItem.Id;
        }

        foreach (var (_, item) in _lists)
        {
            yield return item.Id;
        }
    }
}
