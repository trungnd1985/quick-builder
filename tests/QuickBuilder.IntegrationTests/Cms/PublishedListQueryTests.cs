using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// A Collection list's query over published items: every operator and type, missing values,
/// dates, sorts, paging, and only this site's published data (016 binding-expression.md §5,
/// research TB-08; tasks T060).
/// </summary>
/// <remarks>
/// Posts (fields as in <see cref="TemplatesWorld"/>):
/// <list type="bullet">
/// <item><c>alpha</c>: "Café crème", 5 min, featured, 2026-01-05, News, by Ann, related Ann and Bob, a cover.</item>
/// <item><c>beta</c>: "Linen guide", 12 min, not featured, 2026-02-10, Guides, by Bob, related Bob.</item>
/// <item><c>gamma</c>: "Plain", and nothing else.</item>
/// <item><c>draft-only</c>: never published, featured; and alpha's draft has changed since.</item>
/// </list>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PublishedListQueryTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _ann;
    private Guid _bob;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "list-query");
        _ann = ItemsApi.Id(await _world.PublishedPersonAsync("Ann"));
        _bob = ItemsApi.Id(await _world.PublishedPersonAsync("Bob"));

        var alpha = await _world.PublishedPostAsync("alpha", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Café crème",
            ["f3"] = 5,
            ["f4"] = true,
            ["f5"] = "2026-01-05",
            ["f6"] = new JsonObject { ["mediaId"] = _world.Items.Image.ToString() },
            ["f8"] = "o1",
            ["f9"] = _ann.ToString(),
            ["f10"] = new JsonArray(_ann.ToString(), _bob.ToString()),
        });
        await _world.PublishedPostAsync("beta", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Linen guide",
            ["f3"] = 12,
            ["f4"] = false,
            ["f5"] = "2026-02-10",
            ["f8"] = "o2",
            ["f9"] = _bob.ToString(),
            ["f10"] = new JsonArray(_bob.ToString()),
        });
        await _world.PublishedPostAsync("gamma", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Plain" });
        await _world.Items.PostAsync("draft-only", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Cafe draft", ["f4"] = true });

        // Published values stay as they were when the draft moves on.
        await ItemsApi.SaveOkAsync(_world.Admin, _world.Site, _world.Posts, ItemsApi.Id(alpha),
            ItemsApi.Draft("alpha", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Changed draft", ["f3"] = 99 }, _world.Items.PostsSchemaRevision, ItemsApi.Revision(alpha)));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private static JsonElement Literal(object value) => JsonSerializer.SerializeToElement(value);

    private static ListFilter Field(string id, ListOperator op, object? value = null) =>
        new(ListTarget.Field(id), op, value is null ? null : Literal(value));

    private async Task<(List<string> Slugs, int Total)> QueryAsync(
        IReadOnlyList<ListFilter>? filters = null,
        IReadOnlyList<ListSort>? sorts = null,
        int offset = 0,
        int count = 100,
        IReadOnlyList<Guid>? itemIds = null,
        Guid? site = null)
    {
        var siteId = site ?? _world.Site;
        var page = await _world.WithItemReaderAsync(
            r => r.QueryAsync(siteId, new PublishedListQuery(_world.Posts, itemIds, filters ?? [], sorts ?? [new ListSort(ListTarget.Item(ListBuiltIn.Slug), false)], offset, count)),
            siteId);
        return ([.. page.Items.Select(i => i.Slug)], page.Total);
    }

    private async Task<List<string>> SlugsAsync(params ListFilter[] filters) => (await QueryAsync(filters)).Slugs;

    [Fact]
    public async Task Text_matches_exactly_and_contains_ignores_case_and_accents()
    {
        Assert.Equal(["beta"], await SlugsAsync(Field("f1", ListOperator.Eq, $"{PagesApi.CanaryMarker}Linen guide")));
        Assert.Equal(["alpha", "gamma"], await SlugsAsync(Field("f1", ListOperator.Ne, $"{PagesApi.CanaryMarker}Linen guide")));
        Assert.Equal(["alpha"], await SlugsAsync(Field("f1", ListOperator.Contains, "CAFE CREME")));
        Assert.Equal(["alpha"], await SlugsAsync(Field("f1", ListOperator.Contains, "crème")));
        Assert.Empty(await SlugsAsync(Field("f1", ListOperator.Contains, "changed draft")));
        Assert.Empty(await SlugsAsync(Field("f1", ListOperator.Contains, "100%_")));
    }

    [Fact]
    public async Task Numbers_compare_and_missing_values_match_only_ne_and_empty()
    {
        Assert.Equal(["alpha"], await SlugsAsync(Field("f3", ListOperator.Eq, 5)));
        Assert.Equal(["beta", "gamma"], await SlugsAsync(Field("f3", ListOperator.Ne, 5)));
        Assert.Equal(["beta"], await SlugsAsync(Field("f3", ListOperator.Gt, 5)));
        Assert.Equal(["alpha"], await SlugsAsync(Field("f3", ListOperator.Lt, 12)));
        Assert.Equal(["alpha", "beta"], await SlugsAsync(Field("f3", ListOperator.Lt, 12.5)));
        Assert.Equal(["gamma"], await SlugsAsync(Field("f3", ListOperator.Empty)));
        Assert.Equal(["alpha", "beta"], await SlugsAsync(Field("f3", ListOperator.NotEmpty)));
    }

    [Fact]
    public async Task Dates_compare_by_day()
    {
        Assert.Equal(["alpha"], await SlugsAsync(Field("f5", ListOperator.Eq, "2026-01-05")));
        Assert.Equal(["beta"], await SlugsAsync(Field("f5", ListOperator.Gt, "2026-01-05")));
        Assert.Equal(["alpha"], await SlugsAsync(Field("f5", ListOperator.Lt, "2026-02-10")));
        Assert.Empty(await SlugsAsync(Field("f5", ListOperator.Gt, "2026-02-10")));
        Assert.Equal(["gamma"], await SlugsAsync(Field("f5", ListOperator.Empty)));

        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(["alpha", "beta", "gamma"], await SlugsAsync(new ListFilter(ListTarget.Item(ListBuiltIn.FirstPublishedAt), ListOperator.Eq, Literal(today))));
        Assert.Equal(["alpha", "beta", "gamma"], await SlugsAsync(new ListFilter(ListTarget.Item(ListBuiltIn.LastPublishedAt), ListOperator.Lt, Literal(tomorrow))));
        Assert.Empty(await SlugsAsync(new ListFilter(ListTarget.Item(ListBuiltIn.FirstPublishedAt), ListOperator.Gt, Literal(today))));
    }

    [Fact]
    public async Task A_date_with_a_time_compares_by_its_utc_day()
    {
        var events = await _world.Cms.CollectionAsync("events", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "starts", "date", settings: new { withTime = true })),
        });
        var eventsId = events.GetProperty("id").GetGuid();
        var revision = events.GetProperty("schemaRevision").GetInt32();
        foreach (var (slug, starts) in new[] { ("late", "2026-01-05T23:30:00Z"), ("next", "2026-01-06T00:00:00Z") })
        {
            var created = await ItemsApi.CreateOkAsync(_world.Admin, _world.Site, eventsId, ItemsApi.Draft(slug, new JsonObject { ["f1"] = starts }, revision));
            await ItemsApi.PublishOkAsync(_world.Admin, _world.Site, eventsId, ItemsApi.Id(created), ItemsApi.Revision(created));
        }

        async Task<List<string>> On(ListOperator op, string day) =>
            [.. (await _world.WithItemReaderAsync(r => r.QueryAsync(_world.Site, new PublishedListQuery(
                eventsId, null, [Field("f1", op, day)], [new ListSort(ListTarget.Item(ListBuiltIn.Slug), false)], 0, 10)))).Items.Select(i => i.Slug)];

        Assert.Equal(["late"], await On(ListOperator.Eq, "2026-01-05"));
        Assert.Equal(["next"], await On(ListOperator.Gt, "2026-01-05"));
        Assert.Equal(["late"], await On(ListOperator.Lt, "2026-01-06"));
    }

    [Fact]
    public async Task Booleans_count_no_value_as_false()
    {
        Assert.Equal(["alpha"], await SlugsAsync(Field("f4", ListOperator.Eq, true)));
        Assert.Equal(["beta", "gamma"], await SlugsAsync(Field("f4", ListOperator.Eq, false)));
    }

    [Fact]
    public async Task Options_and_references_match_by_id()
    {
        Assert.Equal(["alpha"], await SlugsAsync(Field("f8", ListOperator.Eq, "o1")));
        Assert.Equal(["beta", "gamma"], await SlugsAsync(Field("f8", ListOperator.Ne, "o1")));
        Assert.Equal(["gamma"], await SlugsAsync(Field("f8", ListOperator.Empty)));

        Assert.Equal(["alpha"], await SlugsAsync(Field("f9", ListOperator.Eq, _ann.ToString())));
        Assert.Equal(["beta", "gamma"], await SlugsAsync(Field("f9", ListOperator.Ne, _ann.ToString())));
        Assert.Equal(["alpha", "beta"], await SlugsAsync(Field("f10", ListOperator.Contains, _bob.ToString())));
        Assert.Equal(["alpha"], await SlugsAsync(Field("f10", ListOperator.Contains, _ann.ToString())));
        Assert.Equal(["gamma"], await SlugsAsync(Field("f10", ListOperator.Empty)));
        Assert.Equal(["alpha", "beta"], await SlugsAsync(Field("f10", ListOperator.NotEmpty)));
    }

    [Fact]
    public async Task Media_fields_take_only_empty_and_not_empty_and_the_slug_eq_and_ne()
    {
        Assert.Equal(["alpha"], await SlugsAsync(Field("f6", ListOperator.NotEmpty)));
        Assert.Equal(["beta", "gamma"], await SlugsAsync(Field("f6", ListOperator.Empty)));
        Assert.Equal(["gamma"], await SlugsAsync(new ListFilter(ListTarget.Item(ListBuiltIn.Slug), ListOperator.Eq, Literal("gamma"))));
        Assert.Equal(["alpha", "beta"], await SlugsAsync(new ListFilter(ListTarget.Item(ListBuiltIn.Slug), ListOperator.Ne, Literal("gamma"))));
    }

    [Fact]
    public async Task A_broken_filter_matches_nothing()
    {
        var featured = Field("f4", ListOperator.Eq, true);

        var unknown = await QueryAsync([featured, Field("f99", ListOperator.Eq, "x")]);
        Assert.Empty(unknown.Slugs);
        Assert.Equal(0, unknown.Total);
        Assert.Empty(await SlugsAsync(featured, Field("f3", ListOperator.Contains, "5")));
        Assert.Empty(await SlugsAsync(featured, Field("f3", ListOperator.Eq, "five")));
        Assert.Empty(await SlugsAsync(featured, Field("f8", ListOperator.Eq, "o9")));
        Assert.Empty(await SlugsAsync(featured, Field("f5", ListOperator.Eq, "5 January")));
        Assert.Empty(await SlugsAsync(featured, Field("f6", ListOperator.Eq, "x")));
        Assert.Empty(await SlugsAsync(featured, Field("f1", ListOperator.Eq, new string('x', 501))));
        Assert.Empty(await SlugsAsync(featured, ListFilter.Never));
    }

    [Fact]
    public async Task Sorts_put_missing_values_last_and_the_id_breaks_ties()
    {
        var byReading = (await QueryAsync(sorts: [new ListSort(ListTarget.Field("f3"), true)])).Slugs;
        var byReadingAsc = (await QueryAsync(sorts: [new ListSort(ListTarget.Field("f3"), false)])).Slugs;
        var byCategoryThenSlug = (await QueryAsync(sorts:
            [new ListSort(ListTarget.Field("f8"), false), new ListSort(ListTarget.Item(ListBuiltIn.Slug), true)])).Slugs;

        Assert.Equal(["beta", "alpha", "gamma"], byReading);
        Assert.Equal(["alpha", "beta", "gamma"], byReadingAsc);
        Assert.Equal(["alpha", "beta", "gamma"], byCategoryThenSlug);

        // No sort at all is still a stable order.
        Assert.Equal((await QueryAsync(sorts: [])).Slugs, (await QueryAsync(sorts: [])).Slugs);
    }

    [Fact]
    public async Task Offset_and_count_page_through_and_the_total_counts_every_match()
    {
        var first = await QueryAsync(offset: 0, count: 2);
        var second = await QueryAsync(offset: 2, count: 2);
        var beyond = await QueryAsync(offset: 9, count: 2);

        Assert.Equal(["alpha", "beta"], first.Slugs);
        Assert.Equal(["gamma"], second.Slugs);
        Assert.Empty(beyond.Slugs);
        Assert.Equal([3, 3, 3], new[] { first.Total, second.Total, beyond.Total });
    }

    [Fact]
    public async Task Item_ids_keep_their_order_without_a_sort_and_only_published_ones_are_listed()
    {
        var all = await _world.WithItemReaderAsync(r => r.QueryAsync(_world.Site, new PublishedListQuery(_world.Posts, null, [], [], 0, 100)));
        var id = all.Items.ToDictionary(i => i.Slug, i => i.Id);
        var draftOnly = Guid.NewGuid();

        var ordered = await QueryAsync(sorts: [], itemIds: [id["gamma"], draftOnly, id["alpha"], id["beta"]]);
        var paged = await QueryAsync(sorts: [], itemIds: [id["gamma"], id["alpha"], id["beta"]], offset: 1, count: 1);

        Assert.Equal(["gamma", "alpha", "beta"], ordered.Slugs);
        Assert.Equal(3, ordered.Total);
        Assert.Equal(["alpha"], paged.Slugs);
        Assert.Equal(3, paged.Total);
    }

    [Fact]
    public async Task Only_published_values_of_this_site_are_listed()
    {
        var page = await _world.WithItemReaderAsync(r => r.QueryAsync(_world.Site, new PublishedListQuery(_world.Posts, null, [], [], 0, 100)));

        Assert.DoesNotContain(page.Items, i => i.Slug == "draft-only");
        var alpha = Assert.Single(page.Items, i => i.Slug == "alpha");
        Assert.Equal(5, alpha.Values.GetProperty("values").GetProperty("f3").GetInt32());
        Assert.Equal($"{PagesApi.CanaryMarker}Café crème", alpha.Name);

        // Asked through site B, site A's collection has no items.
        var throughB = await QueryAsync(site: _world.Cms.SiteB);
        Assert.Empty(throughB.Slugs);
        Assert.Equal(0, throughB.Total);
    }
}
