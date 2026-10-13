using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.UnitTests.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Lists;

/// <summary>
/// Lists inside a list's card are fetched once per listed item with its scope open, identical
/// inner queries run once, and a render makes at most 200 list queries (016 US6, research TB-09;
/// tasks T076, T078).
/// </summary>
/// <remarks>
/// The world is the binding contract's <c>rich-image-link</c> case: posts (<c>…c001</c>) and people
/// (<c>…c002</c>), both titled by <c>f1</c>. The outer list lists posts; the inner one, in its card,
/// lists people named like the card's post.
/// </remarks>
public sealed class NestedListFetchTests
{
    private const string Posts = "0192f5a0-0000-7000-8000-00000000c001";
    private const string People = "0192f5a0-0000-7000-8000-00000000c002";

    private static readonly ResolveCase World = ResolveCase.Load("rich-image-link");

    private static readonly JsonElement Document = JsonDocument.Parse($$$"""
        {"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"outer","type":"collection-list","props":{"source":{"collectionId":"{{{Posts}}}"}},"children":[
            {"id":"outer-card","type":"list-card","props":{},"children":[
              {"id":"inner","type":"collection-list","props":{"source":{"collectionId":"{{{People}}}"},
                "filters":[{"field":"f1","op":"eq","value":{"field":"f1","scope":"outer"}}]},"children":[
                {"id":"inner-card","type":"list-card","props":{},"children":[]},
                {"id":"inner-empty","type":"list-empty","props":{},"children":[]}]}]},
            {"id":"outer-empty","type":"list-empty","props":{},"children":[]}]}]}}
        """).RootElement.Clone();

    private static PublishedItem Item(string collection, string title, int n) => new(
        Guid.Parse($"0192f5a0-0000-7000-8000-{n:x12}"),
        Guid.Parse(collection),
        $"item-{n}",
        title,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        JsonSerializer.SerializeToElement(new { schemaVersion = 1, values = new { f1 = title } }));

    private static async Task<(FetchedBindings Fetched, FakeItems Reader)> FetchAsync(params string[] postTitles)
    {
        var reader = new FakeItems([.. postTitles.Select((title, i) => Item(Posts, title, i + 1))]);
        var fetcher = new BindingFetcher(reader, new FakePages());
        var fetched = await fetcher.FetchAsync(Guid.NewGuid(), [Document], pageItem: null, pageNumbers: null, listPath: "/", CancellationToken.None);
        return (fetched, reader);
    }

    [Fact]
    public async Task An_inner_list_is_fetched_per_card_with_the_cards_item_and_identical_queries_run_once()
    {
        var (fetched, reader) = await FetchAsync("Same", "Same", "Other");

        // One outer query, then one per distinct outer value: "Same" and "Other".
        Assert.Equal(3, reader.Queries.Count);
        Assert.Equal(["Same", "Other"], reader.Queries.Skip(1).Select(q => q.Filters[0].Value!.Value.GetString()));
        Assert.Null(fetched.SkippedLists is { Count: > 0 } ? fetched.SkippedLists : null);

        // Each card has its own result under the inner list's id and its item.
        foreach (var outer in fetched.Data.Lists[ResolvedData.ListKey("outer", [])].Items)
        {
            var inner = fetched.Data.Lists[ResolvedData.ListKey("inner", [outer.Id])];
            Assert.Equal(outer.Name, Assert.Single(inner.Items).Name);
        }
    }

    [Fact]
    public async Task A_render_makes_at_most_two_hundred_list_queries_and_says_which_lists_were_left_empty()
    {
        var (fetched, reader) = await FetchAsync([.. Enumerable.Range(1, 250).Select(n => $"Post {n}")]);

        Assert.Equal(ListQueries.MaxQueriesPerRender, reader.Queries.Count);
        Assert.NotNull(fetched.SkippedLists);
        Assert.All(fetched.SkippedLists!, id => Assert.Equal("inner", id));
        Assert.Equal(51, fetched.SkippedLists!.Count);

        // A list past the budget shows its empty state, never an error.
        var last = fetched.Data.Lists[ResolvedData.ListKey("outer", [])].Items[^1];
        Assert.Empty(fetched.Data.Lists[ResolvedData.ListKey("inner", [last.Id])].Items);
    }

    /// <summary>Lists posts, and people named like a filter's value; records every query.</summary>
    private sealed class FakeItems(IReadOnlyList<PublishedItem> posts) : IPublishedItemReader
    {
        public List<PublishedListQuery> Queries { get; } = [];

        public Task<PublishedItemPage> QueryAsync(Guid siteId, PublishedListQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (query.CollectionId == Guid.Parse(Posts))
            {
                return Task.FromResult(new PublishedItemPage(posts, posts.Count));
            }

            var name = query.Filters.Count > 0 ? query.Filters[0].Value?.GetString() ?? "nobody" : "nobody";
            return Task.FromResult(new PublishedItemPage([Item(People, name, 100_000 + Queries.Count)], 1));
        }

        public Task<IReadOnlyDictionary<Guid, JsonElement>> GetSchemasAsync(
            Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default)
        {
            var schemas = World.Root.GetProperty("schemas");
            IReadOnlyDictionary<Guid, JsonElement> found = collectionIds
                .Where(id => schemas.TryGetProperty(id.ToString(), out _))
                .ToDictionary(id => id, id => schemas.GetProperty(id.ToString()).Clone());
            return Task.FromResult(found);
        }

        public Task<IReadOnlyDictionary<Guid, PublishedItem>> GetAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, PublishedItem>>(new Dictionary<Guid, PublishedItem>());

        public Task<PublishedItemRef?> FindBySlugAsync(Guid siteId, Guid collectionId, string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<PublishedItemRef?>(null);

        public Task<int> CountPublishedAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<PublishedItemAddress>> ListAddressesAsync(
            Guid siteId, Guid collectionId, string? hideFieldId, Guid? afterId, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePages : IPublishedContentReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetLiveTemplatePatternsAsync(
            Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<LivePageRef?> FindLiveByPathAsync(Guid siteId, string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LiveTemplateRef?> FindLiveTemplateAsync(Guid siteId, string parentPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LivePageVersion?> GetLivePageAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, LiveDocument>> GetLiveComponentsAsync(
            Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
            Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsHomePageLiveAsync(Guid siteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LiveAncestor>> GetLiveAncestorsAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
