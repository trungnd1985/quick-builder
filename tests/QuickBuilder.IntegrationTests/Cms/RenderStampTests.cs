using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// An item's render stamp moves exactly when what its item page shows may have changed (016
/// research TB-10, data-model §1.4; tasks T051).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RenderStampTests(DatabaseFixture database)
{
    [Fact]
    public async Task The_stamp_moves_when_the_item_is_published_or_unpublished_and_not_on_draft_saves()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "stamp-own");
        var created = await world.Items.PostAsync("post");
        var id = ItemsApi.Id(created);
        var start = await world.RenderStampAsync(id);

        var saved = await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Draft" }, world.Items.PostsSchemaRevision, ItemsApi.Revision(created)));
        Assert.Equal(start, await world.RenderStampAsync(id));

        var published = await ItemsApi.PublishOkAsync(world.Admin, world.Site, world.Posts, id, ItemsApi.Revision(saved));
        var afterPublish = await world.RenderStampAsync(id);
        Assert.True(afterPublish > start);

        await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Later" }, world.Items.PostsSchemaRevision, ItemsApi.Revision(published)));
        Assert.Equal(afterPublish, await world.RenderStampAsync(id));

        var current = await ItemsApi.GetOkAsync(world.Admin, world.Site, world.Posts, id);
        await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(world.Admin, world.Site, world.Posts, id, ItemsApi.Revision(current)));
        Assert.True(await world.RenderStampAsync(id) > afterPublish);
    }

    [Fact]
    public async Task The_stamp_moves_for_items_whose_published_values_reference_a_changed_item()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "stamp-referrers");
        var ann = ItemsApi.Id(await world.PublishedPersonAsync("Ann"));
        var bob = ItemsApi.Id(await world.PublishedPersonAsync("Bob"));

        // Live: written by Ann. Draft only: written by Bob.
        var live = ItemsApi.Id(await world.PublishedPostAsync("live", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Live", ["f9"] = ann.ToString() }));
        var draft = ItemsApi.Id(await world.Items.PostAsync("draft", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Draft", ["f9"] = bob.ToString() }));
        var liveStamp = await world.RenderStampAsync(live);
        var draftStamp = await world.RenderStampAsync(draft);

        var annItem = await ItemsApi.GetOkAsync(world.Admin, world.Site, world.People, ann);
        var annSaved = await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.People, ann,
            ItemsApi.Draft("ann", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Ann B." }, world.Items.PeopleSchemaRevision, ItemsApi.Revision(annItem)));
        Assert.Equal(liveStamp, await world.RenderStampAsync(live));

        var annPublished = await ItemsApi.PublishOkAsync(world.Admin, world.Site, world.People, ann, ItemsApi.Revision(annSaved));
        var afterAnn = await world.RenderStampAsync(live);
        Assert.True(afterAnn > liveStamp);

        await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(world.Admin, world.Site, world.People, ann, ItemsApi.Revision(annPublished)));
        Assert.True(await world.RenderStampAsync(live) > afterAnn);

        // Bob is referenced only by a draft: no item page shows him.
        var bobItem = await ItemsApi.GetOkAsync(world.Admin, world.Site, world.People, bob);
        await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(world.Admin, world.Site, world.People, bob, ItemsApi.Revision(bobItem)));
        Assert.Equal(draftStamp, await world.RenderStampAsync(draft));
    }
}
