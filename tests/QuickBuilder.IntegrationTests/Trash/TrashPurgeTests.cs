using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Permanent delete, emptying and the retention job (021 US5, US6, FR-020 to FR-025, SC-006).
/// </summary>
/// <remarks>
/// An entry is made old by moving its <c>trashed_at_utc</c> back: moving the clock would expire
/// the administrator's session.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class TrashPurgeTests(DatabaseFixture database)
{
    private const string Reason = "Test inspection of the trash.";

    [Fact]
    public async Task The_job_purges_only_what_is_past_retention_and_records_it_as_the_system()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-job").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            old
            young
            """).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/old"], 1).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/young"], 1).ConfigureAwait(true)).ConfigureAwait(true);
        await AgePageAsync(scenario.App, ids["/old"]).ConfigureAwait(true);

        Assert.Equal(1, await scenario.App.Services.GetRequiredService<PagesTrashPurgeService>().PurgeDueAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(0, await scenario.ReadAsync(db => PagesScenario.Everything<Page>(db).IncludingTrash(Reason).CountAsync(p => p.Id == ids["/old"])).ConfigureAwait(true));
        Assert.Equal(1, await scenario.ReadAsync(db => PagesScenario.Everything<Page>(db).IncludingTrash(Reason).CountAsync(p => p.Id == ids["/young"])).ConfigureAwait(true));

        var purged = await scenario.ReadAsync(db => PagesScenario.Everything<PublishLogEntry>(db).SingleAsync(e => e.SubjectId == ids["/old"] && e.Action == LogActions.Purged)).ConfigureAwait(true);
        Assert.Null(purged.ActorUserId);
    }

    [Fact]
    public async Task Two_instances_purge_each_entry_once()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-job-two").ConfigureAwait(true);
        using var second = new QuickBuilderApp(scenario.App.ConnectionString);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"p{i}"))).ConfigureAwait(true);
        foreach (var id in ids.Values)
        {
            await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, id, 1).ConfigureAwait(true)).ConfigureAwait(true);
            await AgePageAsync(scenario.App, id).ConfigureAwait(true);
        }

        var counts = await Task.WhenAll(
            scenario.App.Services.GetRequiredService<PagesTrashPurgeService>().PurgeDueAsync(CancellationToken.None),
            second.Services.GetRequiredService<PagesTrashPurgeService>().PurgeDueAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(ids.Count, counts.Sum());
        Assert.Equal(ids.Count, await scenario.ReadAsync(db => PagesScenario.Everything<PublishLogEntry>(db).CountAsync(e => e.Action == LogActions.Purged)).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_restore_racing_a_permanent_delete_has_exactly_one_winner()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-race").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        await PagesApi.SeedTreeAsync(admin, site, "home*").ConfigureAwait(true);

        for (var round = 0; round < 10; round++)
        {
            var id = (await PagesApi.CreateOkAsync(admin, site, $"r{round}").ConfigureAwait(true)).GetProperty("id").GetGuid();
            await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, id, 1).ConfigureAwait(true)).ConfigureAwait(true);

            var outcome = await Task.WhenAll(
                admin.PostAsJsonAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{id}/restore", UriKind.Relative), new { confirmedAddress = $"/r{round}" }),
                admin.DeleteAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{id}", UriKind.Relative))).ConfigureAwait(true);

            Assert.Single(outcome, r => r.IsSuccessStatusCode);
            var exists = await scenario.ReadAsync(db => PagesScenario.Everything<Page>(db).AnyAsync(p => p.Id == id)).ConfigureAwait(true);
            Assert.Equal(outcome[0].IsSuccessStatusCode, exists);
        }
    }

    [Fact]
    public async Task Emptying_spares_what_was_trashed_later_and_refuses_a_stale_count()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-empty").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            first
            second
            """).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/first"], 1).ConfigureAwait(true)).ConfigureAwait(true);
        var shownAt = DateTimeOffset.UtcNow;
        await Task.Delay(50).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/second"], 1).ConfigureAwait(true)).ConfigureAwait(true);
        var path = new Uri($"/api/v1/pages/sites/{site}/trash/empty", UriKind.Relative);

        await PagesApi.RefusedAsync(
            await admin.PostAsJsonAsync(path, new { trashedUpTo = shownAt, expectedPages = 2, expectedComponents = 0 }).ConfigureAwait(true),
            HttpStatusCode.Conflict, TrashErrorCodes.CountChanged).ConfigureAwait(true);

        var emptied = await PagesApi.OkAsync(await admin.PostAsJsonAsync(path, new { trashedUpTo = shownAt, expectedPages = 1, expectedComponents = 0 }).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(1, emptied.GetProperty("pages").GetInt32());

        var left = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(ids["/second"], Assert.Single(left.GetProperty("entries").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_job_purges_items_with_their_history_and_files_with_their_bytes()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-job-kinds");
        var (admin, site, app) = (scenario.Admin, scenario.Site, scenario.Cms.App);
        var post = await scenario.PostAsync("old-post", new JsonObject { ["f1"] = "zz-canary-old" });
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post)));
        var file = await MediaApi.UploadOkAsync(admin, site);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, file.Id));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CmsDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE cms.items SET trashed_at_utc = now() - interval '40 days' WHERE id = {ItemsApi.Id(post)}");
            await scope.ServiceProvider.GetRequiredService<MediaDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE media.media_files SET trashed_at_utc = now() - interval '40 days' WHERE id = {file.Id}");
        }

        Assert.Equal(1, await app.Services.GetRequiredService<CmsTrashPurgeService>().PurgeDueAsync(CancellationToken.None));
        Assert.Equal(1, await app.Services.GetRequiredService<MediaTrashPurgeService>().PurgeDueAsync(CancellationToken.None));

        var items = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/cms/sites/{site}/trash/items", UriKind.Relative)));
        Assert.Equal(0, items.GetProperty("total").GetInt32());
        var files = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/media/sites/{site}/trash/files", UriKind.Relative)));
        Assert.Equal(0, files.GetProperty("total").GetInt32());
    }

    private static async Task AgePageAsync(QuickBuilderApp app, Guid id)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PagesDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE pages.pages SET trashed_at_utc = now() - interval '40 days' WHERE id = {id}")
            .ConfigureAwait(false);
    }
}
