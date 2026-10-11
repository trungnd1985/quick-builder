using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// The public path's reader of published items: published data only, read-only, one site
/// (016 research TB-08, Principle VII; tasks T016).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublishedItemReaderTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_item_is_found_by_its_published_slug_only()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "reader-slug");
        var published = await world.PublishedPostAsync("hello-world");
        var draftOnly = await world.Items.PostAsync("draft-post");

        // Changing the draft's slug leaves the published slug as it was until the next publish.
        await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(published),
            ItemsApi.Draft("renamed", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}renamed" }, world.Items.PostsSchemaRevision, ItemsApi.Revision(published)));

        var found = await world.WithItemReaderAsync(r => r.FindBySlugAsync(world.Site, world.Posts, "hello-world"));
        var renamed = await world.WithItemReaderAsync(r => r.FindBySlugAsync(world.Site, world.Posts, "renamed"));
        var draft = await world.WithItemReaderAsync(r => r.FindBySlugAsync(world.Site, world.Posts, "draft-post"));
        var wrongCollection = await world.WithItemReaderAsync(r => r.FindBySlugAsync(world.Site, world.People, "hello-world"));

        Assert.Equal(ItemsApi.Id(published), found?.ItemId);
        Assert.True(found!.RenderStamp >= 2);
        Assert.Null(renamed);
        Assert.Null(draft);
        Assert.Null(wrongCollection);
        Assert.NotEqual(Guid.Empty, ItemsApi.Id(draftOnly));
    }

    [Fact]
    public async Task Get_returns_published_values_and_the_published_name_never_the_draft()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "reader-get");
        var published = await world.PublishedPostAsync("post", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Live title", ["f3"] = 7 });
        await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(published),
            ItemsApi.Draft("post", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Draft title", ["f3"] = 9 }, world.Items.PostsSchemaRevision, ItemsApi.Revision(published)));
        var draftOnly = ItemsApi.Id(await world.Items.PostAsync("draft-only"));

        var items = await world.WithItemReaderAsync(r => r.GetAsync(world.Site, [ItemsApi.Id(published), draftOnly]));

        var item = Assert.Single(items.Values);
        Assert.Equal($"{PagesApi.CanaryMarker}Live title", item.Name);
        Assert.Equal(7, item.Values.GetProperty("values").GetProperty("f3").GetInt32());
        Assert.Equal("post", item.Slug);
        Assert.Equal(world.Posts, item.CollectionId);
        Assert.True(item.FirstPublishedAtUtc <= item.LastPublishedAtUtc);
    }

    [Fact]
    public async Task Schemas_and_counts_are_read_for_the_site()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "reader-schema");
        await world.PublishedPostAsync("one");
        await world.PublishedPostAsync("two");
        await world.Items.PostAsync("draft");

        var schemas = await world.WithItemReaderAsync(r => r.GetSchemasAsync(world.Site, [world.Posts, world.People, Guid.NewGuid()]));
        var count = await world.WithItemReaderAsync(r => r.CountPublishedAsync(world.Site, world.Posts));

        Assert.Equal([world.People, world.Posts], schemas.Keys.Order());
        Assert.Equal("f1", schemas[world.Posts].GetProperty("titleFieldId").GetString());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Another_sites_items_and_schemas_are_absent()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "reader-isolation");
        var published = await world.PublishedPostAsync("hello-world");

        // Read as site B's visitors read: the resolved site is B.
        var found = await world.WithItemReaderAsync(r => r.FindBySlugAsync(world.Site, world.Posts, "hello-world"), world.Cms.SiteB);
        var items = await world.WithItemReaderAsync(r => r.GetAsync(world.Site, [ItemsApi.Id(published)]), world.Cms.SiteB);
        var schemas = await world.WithItemReaderAsync(r => r.GetSchemasAsync(world.Site, [world.Posts]), world.Cms.SiteB);
        var count = await world.WithItemReaderAsync(r => r.CountPublishedAsync(world.Cms.SiteB, world.Posts));

        Assert.Null(found);
        Assert.Empty(items);
        Assert.Empty(schemas);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task The_database_refuses_a_write_through_the_reader_context()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "reader-readonly");
        await using var scope = world.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(world.Site);
        var context = scope.ServiceProvider.GetRequiredService<CmsPublishedReadDbContext>();

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlRawAsync("UPDATE cms.items SET render_stamp = render_stamp + 1"));

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
}
