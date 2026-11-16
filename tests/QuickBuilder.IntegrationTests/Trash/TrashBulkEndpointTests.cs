using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// The success paths of the bulk and per-kind trash endpoints (021 FR-019 to FR-022): each entry
/// is handled on its own, and one that cannot be is named in <c>failed</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrashBulkEndpointTests(DatabaseFixture database)
{
    private static Uri At(string path) => new(path, UriKind.Relative);

    private static async Task<int> TotalAsync(HttpClient client, string list) =>
        (await PagesApi.OkAsync(await client.GetAsync(At(list)).ConfigureAwait(false)).ConfigureAwait(false)).GetProperty("total").GetInt32();

    private static Guid[] Ids(JsonElement outcome, string property) =>
        [.. outcome.GetProperty(property).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetGuid() : e.GetProperty("id").GetGuid())];

    [Fact]
    public async Task Purging_several_pages_deletes_them_and_names_the_one_it_could_not()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-purge-pages").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var page = (await PagesApi.SeedTreeAsync(admin, site, "gone").ConfigureAwait(true))["/gone"];
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, page, 1).ConfigureAwait(true)).ConfigureAwait(true);
        var unknown = Guid.NewGuid();

        var outcome = await PagesApi.OkAsync(await admin.PostAsJsonAsync(
            At($"/api/v1/pages/sites/{site}/trash/purge"),
            new { entries = new[] { new { kind = "page", id = page }, new { kind = "page", id = unknown } } }).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal([page], Ids(outcome, "succeeded"));
        Assert.Equal([unknown], Ids(outcome, "failed"));
        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/pages/sites/{site}/trash/pages").ConfigureAwait(true));
    }

    [Fact]
    public async Task Purging_several_items_or_files_deletes_them_and_names_the_one_it_could_not()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-purge-kinds");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("purged-post");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));
        var file = await MediaApi.UploadOkAsync(admin, site);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, file.Id));
        var unknown = Guid.NewGuid();

        var items = await PagesApi.OkAsync(await admin.PostAsJsonAsync(At($"/api/v1/cms/sites/{site}/trash/purge"), new { ids = new[] { ItemsApi.Id(post), unknown } }));
        var files = await PagesApi.OkAsync(await admin.PostAsJsonAsync(At($"/api/v1/media/sites/{site}/trash/purge"), new { ids = new[] { file.Id, unknown } }));

        Assert.Equal([ItemsApi.Id(post)], Ids(items, "succeeded"));
        Assert.Equal([unknown], Ids(items, "failed"));
        Assert.Equal([file.Id], Ids(files, "succeeded"));
        Assert.Equal([unknown], Ids(files, "failed"));
        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/cms/sites/{site}/trash/items"));
        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/media/sites/{site}/trash/files"));
    }

    [Fact]
    public async Task A_bulk_restore_of_items_restores_what_matches_and_names_what_does_not()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-bulk-items");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var first = await scenario.PostAsync("first-post");
        var second = await scenario.PostAsync("second-post");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(first), ItemsApi.Revision(first)));
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(second), ItemsApi.Revision(second)));

        var outcome = await PagesApi.OkAsync(await admin.PostAsJsonAsync(At($"/api/v1/cms/sites/{site}/trash/restore"), new
        {
            entries = new[]
            {
                new { id = ItemsApi.Id(first), confirmedAddress = "first-post" },
                new { id = ItemsApi.Id(second), confirmedAddress = "not-what-was-shown" },
            },
        }));

        Assert.Equal([ItemsApi.Id(first)], Ids(outcome, "succeeded"));
        Assert.Equal([ItemsApi.Id(second)], Ids(outcome, "failed"));
        Assert.Equal(HttpStatusCode.OK, (await ItemsApi.GetAsync(admin, site, scenario.Posts, ItemsApi.Id(first))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ItemsApi.GetAsync(admin, site, scenario.Posts, ItemsApi.Id(second))).StatusCode);
    }

    [Fact]
    public async Task A_bulk_restore_of_files_restores_what_matches_and_names_what_does_not()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-bulk-files").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var first = await MediaApi.UploadOkAsync(admin, site).ConfigureAwait(true);
        var second = await MediaApi.UploadOkAsync(admin, site).ConfigureAwait(true);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, first.Id).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, second.Id).ConfigureAwait(true)).ConfigureAwait(true);

        var outcome = await PagesApi.OkAsync(await admin.PostAsJsonAsync(At($"/api/v1/media/sites/{site}/trash/restore"), new
        {
            entries = new[]
            {
                new { id = first.Id, confirmedAddress = "top" },
                new { id = second.Id, confirmedAddress = "not-what-was-shown" },
            },
        }).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal([first.Id], Ids(outcome, "succeeded"));
        Assert.Equal([second.Id], Ids(outcome, "failed"));
        Assert.Equal(1, await TotalAsync(admin, $"/api/v1/media/sites/{site}/trash/files").ConfigureAwait(true));
    }

    [Fact]
    public async Task Emptying_the_item_and_file_trash_says_how_many_went_and_refuses_a_stale_count()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-empty-kinds");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("emptied-post");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));
        var file = await MediaApi.UploadOkAsync(admin, site);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, file.Id));
        var upTo = DateTimeOffset.UtcNow.AddMinutes(1);
        var (items, files) = (At($"/api/v1/cms/sites/{site}/trash/empty"), At($"/api/v1/media/sites/{site}/trash/empty"));

        await PagesApi.RefusedAsync(await admin.PostAsJsonAsync(items, new { trashedUpTo = upTo, expectedItems = 2 }), HttpStatusCode.Conflict, TrashErrorCodes.CountChanged);
        await PagesApi.RefusedAsync(await admin.PostAsJsonAsync(files, new { trashedUpTo = upTo, expectedFiles = 2 }), HttpStatusCode.Conflict, TrashErrorCodes.CountChanged);

        Assert.Equal(1, (await PagesApi.OkAsync(await admin.PostAsJsonAsync(items, new { trashedUpTo = upTo, expectedItems = 1 }))).GetProperty("items").GetInt32());
        Assert.Equal(1, (await PagesApi.OkAsync(await admin.PostAsJsonAsync(files, new { trashedUpTo = upTo, expectedFiles = 1 }))).GetProperty("files").GetInt32());
        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/cms/sites/{site}/trash/items"));
        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/media/sites/{site}/trash/files"));
    }

    [Fact]
    public async Task An_item_can_be_deleted_permanently_on_its_own()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-purge-item");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("single-post");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(At($"/api/v1/cms/sites/{site}/trash/items/{ItemsApi.Id(post)}"))).StatusCode);
        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/cms/sites/{site}/trash/items"));
    }

    [Fact]
    public async Task A_component_can_be_deleted_permanently_and_the_log_says_who_did_it()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-purge-comp").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var component = (await PagesApi.CreateComponentOkAsync(admin, site, "Banner").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, component).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, component, version).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(At($"/api/v1/pages/sites/{site}/trash/components/{component}")).ConfigureAwait(true)).StatusCode);

        Assert.Equal(0, await TotalAsync(admin, $"/api/v1/pages/sites/{site}/trash/components").ConfigureAwait(true));
        var purged = await scenario.ReadAsync(db => PagesScenario.Everything<PublishLogEntry>(db).SingleAsync(e => e.SubjectId == component && e.Action == LogActions.Purged)).ConfigureAwait(true);
        Assert.NotNull(purged.ActorUserId);
    }

    /// <summary>T065: the filters only Cms and Media have.</summary>
    [Fact]
    public async Task The_item_trash_filters_by_collection()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-list-items");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("listed-post");
        var person = await scenario.PersonAsync("Ann");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.People, ItemsApi.Id(person), ItemsApi.Revision(person)));

        var root = $"/api/v1/cms/sites/{site}/trash/items";
        Assert.Equal(2, await TotalAsync(admin, root));
        var posts = await PagesApi.OkAsync(await admin.GetAsync(At($"{root}?collectionId={scenario.Posts}")));
        Assert.Equal(ItemsApi.Id(post), Assert.Single(posts.GetProperty("entries").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_file_trash_filters_by_folder_and_counts_the_storage_it_lists()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-list-files").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var folder = await LibraryApi.CreateFolderOkAsync(admin, site, "Posters").ConfigureAwait(true);
        var inFolder = (await PagesApi.OkAsync(await LibraryApi.UploadToFolderAsync(admin, site, folder.Id).ConfigureAwait(true), HttpStatusCode.Created).ConfigureAwait(true)).GetProperty("id").GetGuid();
        var atTop = await MediaApi.UploadOkAsync(admin, site).ConfigureAwait(true);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, inFolder).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, atTop.Id).ConfigureAwait(true)).ConfigureAwait(true);

        var root = $"/api/v1/media/sites/{site}/trash/files";
        var all = await PagesApi.OkAsync(await admin.GetAsync(At(root)).ConfigureAwait(true)).ConfigureAwait(true);
        var filtered = await PagesApi.OkAsync(await admin.GetAsync(At($"{root}?folderId={folder.Id}")).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal(inFolder, Assert.Single(filtered.GetProperty("entries").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.True(all.GetProperty("totalBytes").GetInt64() > 0);
    }

    /// <summary>T043: the site read carries the operator's setting, which the admin shows (FR-025).</summary>
    [Fact]
    public async Task The_site_read_says_how_long_the_trash_keeps_things()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-retention").ConfigureAwait(true);
        var site = scenario.SiteA;
        var read = await PagesApi.OkAsync(await QuickBuilder.IntegrationTests.Sites.SitesApi.GetAsync(scenario.Admin, site).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(30, read.GetProperty("trashRetentionDays").GetInt32());

        using var sixty = new QuickBuilderApp(scenario.App.ConnectionString, settings: new Dictionary<string, string> { ["Trash:RetentionDays"] = "60" });
        var user = await IdentitySeed.SeedUserAsync(sixty.Services, administrator: true, displayName: "Trash Tester").ConfigureAwait(true);
        using var admin = await IdentitySeed.CreateSignedInClientAsync(sixty, user.Id).ConfigureAwait(true);

        var configured = await PagesApi.OkAsync(await QuickBuilder.IntegrationTests.Sites.SitesApi.GetAsync(admin, site).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(60, configured.GetProperty("trashRetentionDays").GetInt32());
    }

    /// <summary>T060: an item that lost its image needs attention, and stops needing it once the file is back.</summary>
    [Fact]
    public async Task Restoring_a_file_clears_the_attention_of_the_items_using_it()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-attention");
        var (admin, site, app) = (scenario.Admin, scenario.Site, scenario.Cms.App);
        var post = await scenario.PostAsync("with-image", new JsonObject { ["f1"] = "zz-canary-image", ["f6"] = new JsonObject { ["mediaId"] = scenario.Image.ToString() } });
        var id = ItemsApi.Id(post);
        Assert.False((await ItemsApi.GetOkAsync(admin, site, scenario.Posts, id)).GetProperty("attention").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await UsageScenario.DeleteAsync(admin, site, scenario.Image, confirmInUse: true)).StatusCode);
        await app.DeliverAsync(new QuickBuilder.Media.Contracts.Events.FileDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, site, scenario.Image, null));
        Assert.True((await ItemsApi.GetOkAsync(admin, site, scenario.Posts, id)).GetProperty("attention").GetBoolean());

        var entry = $"/api/v1/media/sites/{site}/trash/files/{scenario.Image}";
        var plan = await PagesApi.OkAsync(await admin.GetAsync(At($"{entry}/restore-plan")));
        await PagesApi.OkAsync(await admin.PostAsJsonAsync(At($"{entry}/restore"), new { confirmedAddress = plan.GetProperty("address").GetString() }));
        await app.DeliverAsync(new QuickBuilder.Media.Contracts.Events.FileRestored(Guid.CreateVersion7(), DateTimeOffset.UtcNow, site, scenario.Image, null));

        Assert.False((await ItemsApi.GetOkAsync(admin, site, scenario.Posts, id)).GetProperty("attention").GetBoolean());
    }
}
