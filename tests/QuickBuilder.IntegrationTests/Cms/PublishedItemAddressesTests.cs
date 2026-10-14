using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Published item addresses for the sitemap, keyset by id, with the hide switch of FR-007a
/// (017 research SE-14).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublishedItemAddressesTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "sitemap-addresses");
        for (var n = 1; n <= 5; n++)
        {
            await _world.PublishedPostAsync($"post-{n}", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Post {n}", ["f4"] = n == 2 });
        }

        await _world.PublishedPostAsync("plain", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Plain" });
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Every_published_item_comes_once_page_by_page()
    {
        var seen = new List<Guid>();
        Guid? after = null;
        while (true)
        {
            var page = await _world.WithItemReaderAsync(reader => reader.ListAddressesAsync(_world.Site, _world.Posts, null, after, 2)).ConfigureAwait(true);
            if (page.Count == 0)
            {
                break;
            }

            seen.AddRange(page.Select(p => p.ItemId));
            after = page[^1].ItemId;
        }

        Assert.Equal(6, seen.Count);
        Assert.Equal(6, seen.Distinct().Count());
    }

    [Fact]
    public async Task The_hide_switch_hides_only_items_where_it_is_true()
    {
        var all = await _world.WithItemReaderAsync(reader => reader.ListAddressesAsync(_world.Site, _world.Posts, "f4", null, 100)).ConfigureAwait(true);

        Assert.Equal(["post-2"], all.Where(a => a.Hidden).Select(a => a.Slug));
        Assert.Contains(all, a => a.Slug == "plain" && !a.Hidden);
    }

    [Fact]
    public async Task Another_site_s_collection_lists_nothing()
    {
        var other = await _world.WithItemReaderAsync(reader => reader.ListAddressesAsync(_world.Cms.SiteB, _world.Posts, null, null, 100)).ConfigureAwait(true);

        Assert.Empty(other);
    }
}
