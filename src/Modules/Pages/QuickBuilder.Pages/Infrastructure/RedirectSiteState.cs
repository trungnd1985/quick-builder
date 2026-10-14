using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain.Redirects;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// A site's pages, live addresses and redirects, loaded once for checking redirect changes
/// (017 research SE-09). An import adds each accepted row, so later rows are checked against it.
/// </summary>
/// <remarks>
/// Item pages count as addresses here, not as live content: whether an item page answers an
/// address would need a lookup per address in Cms. So a chain through a live item page is
/// walked further than visitors would go. At worst, that refuses a loop that live content
/// would have broken. At request time content still wins (FR-016).
/// </remarks>
internal sealed class RedirectSiteState : IRedirectGraph
{
    private readonly Dictionary<Guid, RedirectEntry> _byId = [];
    private readonly Dictionary<string, RedirectEntry> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RedirectEntry> _prefix = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Match, string Path, string Query), Guid> _sources = [];

    public RedirectSiteState(
        Guid siteId,
        string baseUrl,
        IReadOnlyCollection<string> hostNames,
        IReadOnlyDictionary<Guid, PageAddress> pages,
        IReadOnlyDictionary<Guid, string> livePatterns,
        IEnumerable<RedirectEntry> redirects)
    {
        SiteId = siteId;
        BaseUrl = baseUrl;
        HostNames = hostNames;
        Pages = pages;
        LivePatterns = livePatterns;
        LivePaths = pages.Values.Where(p => p.IsLive && !p.IsTemplate).Select(p => p.Path).ToHashSet(StringComparer.Ordinal);
        PageByPath = pages.Where(p => !p.Value.IsTemplate).GroupBy(p => p.Value.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

        foreach (var redirect in redirects)
        {
            Add(redirect);
        }
    }

    public Guid SiteId { get; }

    /// <summary>The site's base URL, for absolute targets on its own primary host name (SE-09).</summary>
    public string BaseUrl { get; }

    public IReadOnlyCollection<string> HostNames { get; }

    /// <summary>Every page, by id, with its current address.</summary>
    public IReadOnlyDictionary<Guid, PageAddress> Pages { get; }

    /// <summary>Live templates' patterns, by collection.</summary>
    public IReadOnlyDictionary<Guid, string> LivePatterns { get; }

    public IReadOnlySet<string> LivePaths { get; }

    public IReadOnlyDictionary<string, Guid> PageByPath { get; }

    /// <summary>Items that redirects target, with their published slug, loaded as needed.</summary>
    public Dictionary<Guid, ItemAddressParts> Items { get; } = [];

    public int Count => _byId.Count;

    public RedirectEntry? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>Every redirect of the site, enabled or not.</summary>
    public IEnumerable<RedirectEntry> Entries() => _byId.Values;

    /// <summary>The redirect with this source, if any.</summary>
    public Guid? SourceTakenBy(string matchKind, string path, string query) =>
        _sources.TryGetValue((matchKind, path, query), out var id) ? id : null;

    public void Add(RedirectEntry redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        Remove(redirect.Id);
        _byId[redirect.Id] = redirect;
        _sources[(redirect.Match, redirect.Path, redirect.Query)] = redirect.Id;
        if (redirect.Enabled && redirect.Query.Length == 0)
        {
            (redirect.Match == RedirectMatchKinds.Exact ? _exact : _prefix)[redirect.Path] = redirect;
        }
    }

    public void Remove(Guid id)
    {
        if (!_byId.Remove(id, out var old))
        {
            return;
        }

        _sources.Remove((old.Match, old.Path, old.Query));
        if (_exact.TryGetValue(old.Path, out var exact) && exact.Id == id)
        {
            _exact.Remove(old.Path);
        }

        if (_prefix.TryGetValue(old.Path, out var prefix) && prefix.Id == id)
        {
            _prefix.Remove(old.Path);
        }
    }

    public string? AddressOf(RedirectTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind switch
        {
            RedirectTargetKinds.Page when target.PageId is { } page && Pages.TryGetValue(page, out var address) && !address.IsTemplate => address.Path,
            RedirectTargetKinds.Item when target.ItemId is { } item
                && Items.TryGetValue(item, out var parts) && parts.PublishedSlug is { } slug
                && LivePatterns.TryGetValue(parts.CollectionId, out var pattern) => ItemAddress(pattern, slug),
            RedirectTargetKinds.Path when target.Path is { } path => RedirectSource.NormalisePath(Uri.UnescapeDataString(path.Split('?', 2)[0])),
            _ => null,
        };
    }

    public bool IsLiveContent(string path) => LivePaths.Contains(path);

    public RedirectHop? Follow(string path, Guid? except)
    {
        if (_exact.TryGetValue(path, out var exact) && exact.Id != except)
        {
            return new RedirectHop(exact.Id, exact.Target, string.Empty, IsExact: true);
        }

        // Longest prefix first: the ancestors of the path, nearest first.
        for (var end = path.LastIndexOf('/'); end > 0; end = path.LastIndexOf('/', end - 1))
        {
            var ancestor = path[..end];
            if (_prefix.TryGetValue(ancestor, out var prefix) && prefix.Id != except)
            {
                return new RedirectHop(prefix.Id, prefix.Target, path[end..], IsExact: false);
            }
        }

        return null;
    }

    /// <summary>A template pattern with the item's slug in place of <c>{slug}</c>.</summary>
    public static string ItemAddress(string pattern, string slug) =>
        pattern.Replace("{slug}", slug, StringComparison.Ordinal);
}

/// <summary>A page's current address, and whether it is live or a template.</summary>
internal sealed record PageAddress(string Path, bool IsLive, bool IsTemplate);

/// <summary>A redirect, as the checks see it.</summary>
internal sealed record RedirectEntry(Guid Id, string Match, string Path, string Query, RedirectTarget Target, bool Enabled);
