using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Trashed content is gone from every list, count, picker and address (021 US2, FR-006, FR-007,
/// SC-004). <see cref="TrashedIdProbeTests"/> covers addressing by id; this covers what lists it.
/// Pages that showed a trashed file are covered by <c>MediaChangeRefreshTests</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrashVisibilityTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_trashed_item_leaves_lists_counts_and_pickers_until_it_is_restored()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-see-item");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("seen");
        var id = ItemsApi.Id(post);

        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, id, ItemsApi.Revision(post)));

        Assert.Equal(0, (await ItemsApi.ListOkAsync(admin, site, scenario.Posts)).GetProperty("total").GetInt32());
        var collection = await PagesApi.OkAsync(await CmsApi.GetAsync(admin, site, scenario.Posts));
        Assert.Equal(0, collection.GetProperty("itemCount").GetInt32());
        var lookup = await PagesApi.OkAsync(await ItemsApi.LookupAsync(admin, site, [id]));
        Assert.Equal(0, lookup.GetProperty("items").GetArrayLength());

        // FR-007: no new reference to it can be made.
        var refused = await ItemsApi.CreateAsync(admin, site, scenario.Posts, ItemsApi.Draft(
            "refers", new JsonObject { ["f1"] = "x", ["f9"] = id.ToString() }, scenario.PostsSchemaRevision));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_trashed_file_answers_not_found_at_its_address_and_leaves_the_library()
    {
        using var scenario = await MediaScenario.CreateAsync(database, "trash-see-file").ConfigureAwait(true);
        var file = await MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true)).StatusCode);

        await PagesApi.OkAsync(await MediaApi.DeleteAsync(scenario.Admin, scenario.SiteA, file.Id).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true)).StatusCode);
        Assert.Empty((await MediaApi.ListOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true)).Items);
    }

    [Fact]
    public async Task A_trashed_pages_draft_cannot_be_written_and_a_trashed_component_cannot_be_used()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-see-page").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            keep
            gone
            """).ConfigureAwait(true);
        var header = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, header).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, header, version).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/gone"], 1).ConfigureAwait(true)).ConfigureAwait(true);

        // A valid document, so only the trash decides the answer.
        await PagesApi.RefusedAsync(
            await PagesApi.SaveDraftAsync(admin, PagesApi.PagePath(site, ids["/gone"]), PagesApi.DocumentReferencing(), 1).ConfigureAwait(true),
            HttpStatusCode.NotFound, "pages.not_found").ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(admin, PagesApi.PagePath(site, ids["/keep"]), PagesApi.DocumentReferencing(header)).ConfigureAwait(true),
            HttpStatusCode.BadRequest, "pages.unknown_component").ConfigureAwait(true);

        var components = await PagesApi.OkAsync(await PagesApi.ListComponentsAsync(admin, site).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.DoesNotContain(header.ToString(), components.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
