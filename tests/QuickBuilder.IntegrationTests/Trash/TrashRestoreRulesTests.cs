using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Restoring when the original place is gone (021 US4, FR-016 to FR-018, FR-023, SC-008), and
/// browsing and restoring several at once (US3, FR-010, FR-019).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrashRestoreRulesTests(DatabaseFixture database)
{
    private static Uri Pages(Guid site, Guid page, string suffix = "") => new($"/api/v1/pages/sites/{site}/trash/pages/{page}{suffix}", UriKind.Relative);

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid site, Guid page, object body) =>
        client.PostAsJsonAsync(Pages(site, page, "/restore"), body);

    private static async Task<JsonElement> PlanAsync(HttpClient client, Guid site, Guid page, string query = "") =>
        await PagesApi.OkAsync(await client.GetAsync(Pages(site, page, "/restore-plan" + query)).ConfigureAwait(false)).ConfigureAwait(false);

    private static async Task<(PagesScenario Scenario, Guid About, Guid Team)> AboutTeamAsync(DatabaseFixture database, string label)
    {
        var scenario = await PagesScenario.CreateAsync(database, label).ConfigureAwait(false);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            home*
            about
              team
            """).ConfigureAwait(false);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/about/team"], 1).ConfigureAwait(false)).ConfigureAwait(false);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/about"], 1).ConfigureAwait(false)).ConfigureAwait(false);
        return (scenario, ids["/about"], ids["/about/team"]);
    }

    [Fact]
    public async Task A_page_whose_parent_is_in_the_trash_needs_a_choice_and_nothing_happens_until_it_is_made()
    {
        var (scenario, about, team) = await AboutTeamAsync(database, "trash-parent").ConfigureAwait(true);
        using var _ = scenario;
        var (admin, site) = (scenario.Admin, scenario.SiteA);

        await PagesApi.RefusedAsync(await admin.GetAsync(Pages(site, team, "/restore-plan")).ConfigureAwait(true), HttpStatusCode.UnprocessableEntity, TrashErrorCodes.PlacementRequired).ConfigureAwait(true);
        await PagesApi.RefusedAsync(await RestoreAsync(admin, site, team, new { confirmedAddress = "/about/team" }).ConfigureAwait(true), HttpStatusCode.UnprocessableEntity, TrashErrorCodes.PlacementRequired).ConfigureAwait(true);

        var top = await PlanAsync(admin, site, team, "?placement=top").ConfigureAwait(true);
        Assert.Equal(["with_parents", "chosen", "top"], top.GetProperty("placementOptions").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal("/team", top.GetProperty("address").GetString());

        var withParents = await PlanAsync(admin, site, team, "?placement=with_parents").ConfigureAwait(true);
        Assert.Equal("/about/team", withParents.GetProperty("address").GetString());
        Assert.Equal(about, Assert.Single(withParents.GetProperty("parentsToRestore").EnumerateArray()).GetProperty("id").GetGuid());

        // Planning changed nothing.
        Assert.Equal(["/"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
    }

    [Fact]
    public async Task Restoring_with_parents_brings_the_whole_branch_back()
    {
        var (scenario, about, team) = await AboutTeamAsync(database, "trash-with-parents").ConfigureAwait(true);
        using var _ = scenario;
        var (admin, site) = (scenario.Admin, scenario.SiteA);

        var result = await PagesApi.OkAsync(await RestoreAsync(admin, site, team, new { placement = "with_parents", confirmedAddress = "/about/team" }).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(2, result.GetProperty("restored").GetArrayLength());
        Assert.Equal(["/", "/about", "/about/team"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
        Assert.Equal(about, (await PagesApi.NodeAsync(admin, site, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_page_whose_parent_is_gone_can_only_be_placed_somewhere_else()
    {
        var (scenario, about, team) = await AboutTeamAsync(database, "trash-parent-gone").ConfigureAwait(true);
        using var _ = scenario;
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Pages(site, about)).ConfigureAwait(true)).StatusCode);

        var plan = await PlanAsync(admin, site, team, "?placement=top").ConfigureAwait(true);
        Assert.Equal(["chosen", "top"], plan.GetProperty("placementOptions").EnumerateArray().Select(o => o.GetString()));

        var list = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("gone", Assert.Single(list.GetProperty("entries").EnumerateArray()).GetProperty("place").GetProperty("parentState").GetString());

        await PagesApi.OkAsync(await RestoreAsync(admin, site, team, new { placement = "top", confirmedAddress = "/team" }).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(["/", "/team"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_chosen_parent_must_be_live()
    {
        var (scenario, about, team) = await AboutTeamAsync(database, "trash-chosen").ConfigureAwait(true);
        using var _ = scenario;
        var (admin, site) = (scenario.Admin, scenario.SiteA);

        await PagesApi.RefusedAsync(
            await admin.GetAsync(Pages(site, team, $"/restore-plan?placement=chosen&parentId={about}")).ConfigureAwait(true),
            HttpStatusCode.UnprocessableEntity, TrashErrorCodes.InvalidPlacement).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_folder_holding_only_trashed_files_can_go_and_the_file_comes_back_to_the_top_level()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-folder").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var folder = await LibraryApi.CreateFolderOkAsync(admin, site, "Old").ConfigureAwait(true);
        var file = await MediaApi.UploadOkAsync(admin, site).ConfigureAwait(true);
        Assert.True((await LibraryApi.MoveAsync(admin, site, [file.Id], folder.Id).ConfigureAwait(true)).IsSuccessStatusCode);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, file.Id).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.True((await LibraryApi.DeleteFolderAsync(admin, site, folder.Id).ConfigureAwait(true)).IsSuccessStatusCode);

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/media/sites/{site}/trash/files/{file.Id}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.True(plan.GetProperty("folderGone").GetBoolean());
        Assert.Equal("top", plan.GetProperty("placement").GetString());
        await PagesApi.OkAsync(await admin.PostAsJsonAsync(new Uri($"/api/v1/media/sites/{site}/trash/files/{file.Id}/restore", UriKind.Relative), new { confirmedAddress = "top" }).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task An_item_that_misses_a_new_required_field_comes_back_needing_attention()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-schema");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var person = await scenario.PersonAsync("Ann");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.People, ItemsApi.Id(person), ItemsApi.Revision(person)));

        var collection = await PagesApi.OkAsync(await CmsApi.GetAsync(admin, site, scenario.People));
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("f1", "name", "text", settings: new { multiline = false, maxLength = 100 }),
                CmsApi.Field("~2", "city", "text", required: true, settings: new { multiline = false, maxLength = 100 })),
        };
        var preview = await CmsApi.PreviewOkAsync(admin, site, scenario.People, CmsApi.SaveBody(collection, schema));
        await CmsApi.SaveOkAsync(admin, site, scenario.People, CmsApi.SaveBody(collection, schema, digest: preview.GetProperty("digest").GetString()));

        var path = $"/api/v1/cms/sites/{site}/trash/items/{ItemsApi.Id(person)}";
        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"{path}/restore-plan", UriKind.Relative)));
        Assert.Equal("city", Assert.Single(plan.GetProperty("schemaIssues").EnumerateArray()).GetString());

        await PagesApi.OkAsync(await admin.PostAsJsonAsync(new Uri($"{path}/restore", UriKind.Relative), new { confirmedAddress = plan.GetProperty("address").GetString() }));
        Assert.True((await ItemsApi.GetOkAsync(admin, site, scenario.People, ItemsApi.Id(person))).GetProperty("attention").GetBoolean());
    }

    [Fact]
    public async Task Deleting_a_collection_counts_its_trashed_items_and_takes_them_with_it()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-collection");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("gone-with-it");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));

        var collection = await PagesApi.OkAsync(await CmsApi.GetAsync(admin, site, scenario.Posts));
        Assert.Equal(0, collection.GetProperty("itemCount").GetInt32());
        Assert.Equal(1, collection.GetProperty("trashedItemCount").GetInt32());

        var deleted = await admin.DeleteAsync(new Uri($"{CmsApi.CollectionPath(site, scenario.Posts)}?revision={collection.GetProperty("revision").GetInt32()}", UriKind.Relative));
        Assert.True(deleted.IsSuccessStatusCode, await deleted.Content.ReadAsStringAsync());

        var trash = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/cms/sites/{site}/trash/items", UriKind.Relative)));
        Assert.Equal(0, trash.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Search_filters_and_paging_narrow_the_trash()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-list").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, string.Join('\n', Enumerable.Range(1, 55).Select(i => i % 2 == 0 ? $"pricing-{i}" : $"other-{i}"))).ConfigureAwait(true);
        foreach (var id in ids.Values)
        {
            await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, id, 1).ConfigureAwait(true)).ConfigureAwait(true);
        }

        var root = $"/api/v1/pages/sites/{site}/trash/pages";
        var first = await PagesApi.OkAsync(await admin.GetAsync(new Uri(root, UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(55, first.GetProperty("total").GetInt32());
        Assert.Equal(50, first.GetProperty("entries").GetArrayLength());

        var second = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"{root}?after={first.GetProperty("nextCursor").GetString()}", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(5, second.GetProperty("entries").GetArrayLength());
        Assert.Empty(first.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .Intersect(second.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())));

        var found = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"{root}?q=pricing", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(27, found.GetProperty("total").GetInt32());

        var none = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"{root}?trashedBy={Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, none.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_bulk_restore_restores_each_entry_or_says_why_not()
    {
        var (scenario, about, team) = await AboutTeamAsync(database, "trash-bulk").ConfigureAwait(true);
        using var _ = scenario;
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var body = new
        {
            entries = new object[]
        {
            new { kind = "page", id = about, confirmedAddress = "/about" },
            new { kind = "page", id = Guid.NewGuid(), confirmedAddress = "/nope" },
            new { kind = "page", id = team, confirmedAddress = "/about/team" },
        }
        };

        var outcome = await PagesApi.OkAsync(await admin.PostAsJsonAsync(new Uri($"/api/v1/pages/sites/{site}/trash/restore", UriKind.Relative), body).ConfigureAwait(true)).ConfigureAwait(true);

        // Team could come back too: About was restored first, so its parent is live again.
        Assert.Equal([about, team], outcome.GetProperty("succeeded").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal(TrashErrorCodes.NotInTrash, Assert.Single(outcome.GetProperty("failed").EnumerateArray()).GetProperty("code").GetString());
    }
}
