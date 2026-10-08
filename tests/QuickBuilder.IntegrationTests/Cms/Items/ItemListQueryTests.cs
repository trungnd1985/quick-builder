using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>Search, filters, sorts and pages of the item list (015 US3, FR-028, FR-029; research IT-13, IT-14).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemListQueryTests(DatabaseFixture database) : IAsyncLifetime
{
    private ItemsScenario _scenario = null!;
    private Guid _ann;
    private Guid _bob;

    public async Task InitializeAsync()
    {
        _scenario = await ItemsScenario.CreateAsync(database, "items-list-query");
        _ann = ItemsApi.Id(await _scenario.PersonAsync("Ann"));
        _bob = ItemsApi.Id(await _scenario.PersonAsync("Bob"));

        // Twelve posts; some with values, some without, so filters and missing-last sorts matter.
        for (var n = 0; n < 12; n++)
        {
            var values = new JsonObject { ["f1"] = n switch { 0 => "Héllo Wörld", 1 => "zz-canary-100%_off sale", _ => $"zz-canary-Post {n:00}" } };
            if (n % 3 != 0)
            {
                values["f3"] = 12 - n;
            }

            if (n % 2 == 0)
            {
                values["f4"] = n % 4 == 0;
                values["f5"] = $"2026-01-{n + 1:00}";
            }

            values["f8"] = n % 2 == 0 ? "o1" : "o2";
            values["f9"] = (n % 2 == 0 ? _ann : _bob).ToString();
            values["f10"] = n < 3 ? new JsonArray(_bob.ToString()) : new JsonArray(_ann.ToString());
            var created = await _scenario.PostAsync($"post-{n:00}", values);

            if (n % 5 == 0)
            {
                created = await ItemsApi.PublishOkAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, ItemsApi.Id(created), ItemsApi.Revision(created));
            }

            if (n == 5)
            {
                await ItemsApi.SaveOkAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, ItemsApi.Id(created),
                    ItemsApi.Draft("post-05", new JsonObject { ["f1"] = "zz-canary-changed" }, _scenario.PostsSchemaRevision, ItemsApi.Revision(created)));
            }
        }
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private async Task<List<string>> SlugsAsync(string query)
    {
        var page = await ItemsApi.ListOkAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, query);
        return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!).ToList();
    }

    [Fact]
    public async Task Search_matches_name_or_slug_ignoring_case_and_accents_and_takes_the_term_literally()
    {
        Assert.Equal(["post-00"], await SlugsAsync("?q=HELLO"));
        Assert.Equal(["post-00"], await SlugsAsync("?q=h%C3%A9llo%20w%C3%B6rld"));
        Assert.Equal(["post-03"], await SlugsAsync("?q=post-03"));
        Assert.Equal(["post-01"], await SlugsAsync("?q=100%25_"));
        Assert.Empty(await SlugsAsync("?q=10_0"));
    }

    [Fact]
    public async Task Filters_combine_and_read_the_draft()
    {
        Assert.Equal(["post-05"], await SlugsAsync("?status=changed"));
        Assert.Equal(2, (await SlugsAsync("?status=published")).Count);
        Assert.Equal(6, (await SlugsAsync("?field[f8]=o1&pageSize=100")).Count);
        Assert.Equal(3, (await SlugsAsync("?field[f4]=true")).Count);

        // A boolean with no value counts as false: 3 false and 6 with no value.
        Assert.Equal(9, (await SlugsAsync("?field[f4]=false")).Count);
        // post-05 draft now holds only a title, so it no longer names Bob.
        Assert.Equal(5, (await SlugsAsync($"?field[f9]={_bob}")).Count);
        Assert.Equal(3, (await SlugsAsync($"?field[f10]={_bob}")).Count);
        Assert.Equal(["post-00", "post-10"], (await SlugsAsync($"?status=published&field[f9]={_ann}&sort=slug&dir=asc")));
    }

    [Fact]
    public async Task Sorts_put_missing_values_last_in_both_directions_with_a_stable_order()
    {
        var ascending = await SlugsAsync("?sort=field.f3&dir=asc&pageSize=100");
        var descending = await SlugsAsync("?sort=field.f3&dir=desc&pageSize=100");

        // Seven drafts hold a reading time (post-05 lost its own); five have none and come last.
        Assert.Equal(["post-11", "post-10", "post-08", "post-07", "post-04", "post-02", "post-01"], ascending.Take(7));
        Assert.Equal(["post-01", "post-02", "post-04", "post-07", "post-08", "post-10", "post-11"], descending.Take(7));
        Assert.Equal(ascending.Skip(7), descending.Skip(7));

        var byDate = await SlugsAsync("?sort=field.f5&dir=desc&pageSize=100");
        Assert.Equal(["post-10", "post-08", "post-06", "post-04", "post-02", "post-00"], byDate.Take(6));

        var byName = await SlugsAsync("?sort=name&dir=asc&pageSize=100");
        Assert.Equal("post-00", byName[0]);
    }

    [Fact]
    public async Task Every_item_appears_on_exactly_one_page()
    {
        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await ItemsApi.ListOkAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, $"?page={page}&pageSize=25&sort=status");
            Assert.Equal(12, result.GetProperty("total").GetInt32());
            seen.AddRange(result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!));
        }

        Assert.Equal(12, seen.Count);
        Assert.Equal(12, seen.Distinct().Count());
    }

    [Theory]
    [InlineData("?field[f1]=x", "field[f1]")]
    [InlineData("?sort=field.f2", "sort")]
    [InlineData("?field[f99]=x", "field[f99]")]
    [InlineData("?pageSize=30", "pageSize")]
    [InlineData("?status=gone", "status")]
    public async Task Queries_that_do_not_fit_the_collection_are_refused(string query, string parameter)
    {
        var refused = await PagesApi.ProblemAsync(await ItemsApi.ListAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, query));
        Assert.Equal((HttpStatusCode.BadRequest, "cms.invalid_query", parameter), (refused.Status, refused.Code, refused.Text("parameter")));
    }

    [Fact]
    public async Task A_search_term_over_100_characters_is_refused()
    {
        var refused = await PagesApi.ProblemAsync(await ItemsApi.ListAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, "?q=" + new string('a', 101)));
        Assert.Equal("cms.invalid_query", refused.Code);
    }

    [Fact]
    public async Task Lookup_names_items_across_collections_and_lists_unknown_ids_as_missing()
    {
        var foreign = Guid.NewGuid();
        var found = await PagesApi.OkAsync(await ItemsApi.LookupAsync(_scenario.Admin, _scenario.Site, [_ann, _bob, foreign]));

        Assert.Equal(2, found.GetProperty("items").GetArrayLength());
        Assert.Equal([foreign], found.GetProperty("missing").EnumerateArray().Select(i => i.GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, (await ItemsApi.LookupAsync(_scenario.Admin, _scenario.Site, Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Saving_a_reference_to_the_item_itself_is_refused()
    {
        var post = await _scenario.PostAsync("self");
        var refused = await PagesApi.ProblemAsync(await ItemsApi.SaveAsync(_scenario.Admin, _scenario.Site, _scenario.Posts, ItemsApi.Id(post),
            ItemsApi.Draft("self", new JsonObject { ["f1"] = "zz-canary-self", ["f10"] = new JsonArray(ItemsApi.Id(post).ToString()) }, _scenario.PostsSchemaRevision, ItemsApi.Revision(post))));
        Assert.Equal([("f10", "reference_self")], ItemsApi.Problems(refused.Body.GetProperty("report")));
    }
}
