using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// An item or a media file moved to the trash comes back as it was (021 US1, SC-001, FR-001 to
/// FR-005, FR-014, FR-015, research TR-04, TR-06).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemAndFileTrashRoundTripTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_deleted_item_frees_its_slug_and_references_and_gets_them_back_on_restore()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-item-rt");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var ann = ItemsApi.Id(await scenario.PersonAsync("Ann"));
        var post = await scenario.PostAsync("hello", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Hello", ["f9"] = ann.ToString() });
        var postId = ItemsApi.Id(post);

        // Ann is referred to, so she cannot be deleted (015 FR-008).
        var annItem = await ItemsApi.GetOkAsync(admin, site, scenario.People, ann);
        Assert.Equal(HttpStatusCode.Conflict, (await ItemsApi.DeleteAsync(admin, site, scenario.People, ann, ItemsApi.Revision(annItem))).StatusCode);

        var deleted = await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, postId, ItemsApi.Revision(post)));
        Assert.Equal(deleted.GetProperty("trashedAtUtc").GetDateTimeOffset().AddDays(30), deleted.GetProperty("purgeOnUtc").GetDateTimeOffset());

        // Gone for every other read; it no longer refers to Ann (TR-06), and its slug is free (TR-04).
        Assert.Equal(HttpStatusCode.NotFound, (await ItemsApi.GetAsync(admin, site, scenario.Posts, postId)).StatusCode);
        var referrers = await PagesApi.OkAsync(await ItemsApi.ReferrersAsync(admin, site, scenario.People, ann));
        Assert.Equal(0, referrers.GetProperty("total").GetInt32());
        Assert.Contains(await scenario.Cms.OutboxAsync(), e => e.Type == "Cms.ItemDeleted");
        var other = await scenario.PostAsync("hello");

        var list = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/cms/sites/{site}/trash/items?collectionId={scenario.Posts}", UriKind.Relative)));
        var entry = Assert.Single(list.GetProperty("entries").EnumerateArray());
        Assert.Equal(postId, entry.GetProperty("id").GetGuid());
        Assert.Equal(scenario.Posts, entry.GetProperty("place").GetProperty("collectionId").GetGuid());

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/cms/sites/{site}/trash/items/{postId}/restore-plan", UriKind.Relative)));
        var slug = plan.GetProperty("address").GetString()!;
        Assert.NotEqual("hello", slug);
        Assert.True(plan.GetProperty("addressChanged").GetBoolean());

        await PagesApi.OkAsync(await RestoreAsync(admin, $"/api/v1/cms/sites/{site}/trash/items/{postId}", slug));

        // Same id and values, the new slug, and the reference to Ann works again.
        var back = await ItemsApi.GetOkAsync(admin, site, scenario.Posts, postId);
        Assert.Equal(slug, back.GetProperty("slug").GetString());
        Assert.Equal(ann.ToString(), ItemsApi.Values(back)["f9"]!.GetValue<string>());
        referrers = await PagesApi.OkAsync(await ItemsApi.ReferrersAsync(admin, site, scenario.People, ann));
        Assert.Equal(1, referrers.GetProperty("total").GetInt32());
        Assert.Contains(await scenario.Cms.OutboxAsync(), e => e.Type == "Cms.ItemRestored");
        Assert.NotEqual(postId, ItemsApi.Id(other));

        await PagesApi.RefusedAsync(
            await RestoreAsync(admin, $"/api/v1/cms/sites/{site}/trash/items/{postId}", slug),
            HttpStatusCode.NotFound, TrashErrorCodes.NotInTrash);
    }

    [Fact]
    public async Task A_published_item_is_unpublished_by_the_delete_and_can_come_back_live()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-item-live");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("live-post");
        var published = await ItemsApi.PublishOkAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post));

        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(published)));

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/cms/sites/{site}/trash/items/{ItemsApi.Id(post)}/restore-plan", UriKind.Relative)));
        Assert.True(plan.GetProperty("wasLive").GetBoolean());
        Assert.True(plan.GetProperty("canRestoreAndPublish").GetBoolean());

        // Publishing again is asked for; the next test shows a plain restore does not.
        var restored = await PagesApi.OkAsync(await RestoreAsync(admin, $"/api/v1/cms/sites/{site}/trash/items/{ItemsApi.Id(post)}", "live-post", publish: true));
        Assert.True(restored.GetProperty("published").GetBoolean());
        var back = await ItemsApi.GetOkAsync(admin, site, scenario.Posts, ItemsApi.Id(post));
        Assert.NotEqual("draft", back.GetProperty("status").GetString());
    }

    /// <summary>Principle VII: a plain restore never publishes (021 FR-014). T021: both steps are in the history.</summary>
    [Fact]
    public async Task A_plain_restore_of_a_published_item_stays_a_draft_and_the_history_records_both_steps()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-item-plain");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var post = await scenario.PostAsync("plain-post");
        var id = ItemsApi.Id(post);
        var published = await ItemsApi.PublishOkAsync(admin, site, scenario.Posts, id, ItemsApi.Revision(post));
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, id, ItemsApi.Revision(published)));

        var restored = await PagesApi.OkAsync(await RestoreAsync(admin, $"/api/v1/cms/sites/{site}/trash/items/{id}", "plain-post"));

        Assert.False(restored.GetProperty("published").GetBoolean());
        Assert.Equal("draft", (await ItemsApi.GetOkAsync(admin, site, scenario.Posts, id)).GetProperty("status").GetString());
        var kinds = (await PagesApi.OkAsync(await ItemsApi.HistoryAsync(admin, site, scenario.Posts, id))).GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Contains("trashed", kinds);
        Assert.Contains("restored", kinds);
    }

    /// <summary>T022: restoring mirrors deleting, and a file a live page shows is for those who may delete it there.</summary>
    [Fact]
    public async Task An_editor_may_restore_an_item_but_not_their_own_file_while_a_live_page_shows_it()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-item-editor");
        var (admin, site) = (scenario.Admin, scenario.Site);
        var editor = await scenario.Cms.UserAsync((site, QuickBuilder.Shared.Kernel.Authorization.SystemRole.Editor));
        var post = await scenario.PostAsync("editor-post");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));
        await PagesApi.OkAsync(await RestoreAsync(editor, $"/api/v1/cms/sites/{site}/trash/items/{ItemsApi.Id(post)}", "editor-post"));

        var file = await MediaApi.UploadOkAsync(editor, site);
        await UsageScenario.PageShowingAsync(admin, site, "showing", file.Id, publish: true);
        Assert.Equal(HttpStatusCode.OK, (await UsageScenario.DeleteAsync(admin, site, file.Id, confirmInUse: true)).StatusCode);

        var refused = await RestoreAsync(editor, $"/api/v1/media/sites/{site}/trash/files/{file.Id}", "top");
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Contains("media.in_live_use", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deleted_file_keeps_its_bytes_and_comes_back_at_the_same_address()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-file-rt").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var file = await MediaApi.UploadOkAsync(admin, site).ConfigureAwait(true);

        var deleted = await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, file.Id).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.True(deleted.TryGetProperty("purgeOnUtc", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri($"{MediaApi.FilesPath(site)}/{file.Id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Contains(await MediaOutboxAsync(scenario).ConfigureAwait(true), type => type == "Media.FileDeleted");

        // FR-012: the media trash says how much storage its files take.
        var list = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/media/sites/{site}/trash/files", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(file.Id, Assert.Single(list.GetProperty("entries").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.True(list.GetProperty("totalBytes").GetInt64() > 0);

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/media/sites/{site}/trash/files/{file.Id}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await RestoreAsync(admin, $"/api/v1/media/sites/{site}/trash/files/{file.Id}", plan.GetProperty("address").GetString()!).ConfigureAwait(true)).ConfigureAwait(true);

        var content = await admin.GetAsync(new Uri($"{MediaApi.FilesPath(site)}/{file.Id}/content", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Contains(await MediaOutboxAsync(scenario).ConfigureAwait(true), type => type == "Media.FileRestored");
    }

    private static async Task<List<string>> MediaOutboxAsync(PagesScenario scenario)
    {
        await using var scope = scenario.App.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<QuickBuilder.Media.Infrastructure.MediaDbContext>();
        return await media.Set<QuickBuilder.Shared.Infrastructure.Outbox.OutboxMessage>().AsNoTracking().Select(m => m.EventType).ToListAsync().ConfigureAwait(false);
    }

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, string entryPath, string confirmedAddress, bool publish = false) =>
        client.PostAsJsonAsync(new Uri($"{entryPath}/restore", UriKind.Relative), new { confirmedAddress, publish });
}
