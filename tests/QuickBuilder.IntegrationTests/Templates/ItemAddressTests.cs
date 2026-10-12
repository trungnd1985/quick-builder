using System.Text.Json;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>Where the admin says visitors find an item, or why they cannot (016 FR-007, research TB-12).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemAddressTests(DatabaseFixture database)
{
    private static async Task<(string? Path, string? Reason)> AddressAsync(TemplatesWorld world, JsonElement item)
    {
        var read = await ItemsApi.GetOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(item));
        var address = read.GetProperty("address");
        return (address.GetProperty("path").GetString(), address.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_item_reports_its_address_or_why_it_has_none()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "item-address");
        var published = await world.PublishedPostAsync("hello-world");
        var draft = await world.Items.PostAsync("draft-post");

        Assert.Equal((null, "no_template"), await AddressAsync(world, published));

        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(post, world.Posts);
        Assert.Equal((null, "template_not_live"), await AddressAsync(world, published));

        await world.PublishDocumentAsync(post, TemplatesWorld.Document(TemplatesWorld.BoundHeading("h1", """{"field":"f1"}""")));
        Assert.Equal(("/blog/hello-world", null), await AddressAsync(world, published));
        Assert.Equal((null, "not_published"), await AddressAsync(world, draft));

        var hiding = await world.PageAsync("hello-world", blog);
        await world.PublishDocumentAsync(hiding, PublishingApi.HeadingDocument("A page"));
        Assert.Equal(("/blog/hello-world", "hidden_by_page"), await AddressAsync(world, published));
    }
}
