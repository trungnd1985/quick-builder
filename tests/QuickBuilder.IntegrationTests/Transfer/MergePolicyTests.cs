using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Importing an archive into a site that already holds some of its documents, with each
/// conflict policy (020 SC-005, FR-022 … FR-027, research ST-10).
/// </summary>
/// <remarks>
/// The target is the source imported as a new site, so every document of the first export has
/// a counterpart there by address or name; the source then gains <c>/news</c>, which has none.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MergePolicyTests(DatabaseFixture database)
{
    [Theory]
    [InlineData("replace")]
    [InlineData("skip")]
    [InlineData("rename")]
    public async Task Each_policy_resolves_the_conflicts_the_check_listed_and_leaves_the_rest_of_the_site_alone(string policy)
    {
        using var site = await FullSite.CreateAsync(database, $"merge-{policy}").ConfigureAwait(true);

        // A folder both will hold, with a file in it.
        var docs = await QuickBuilder.IntegrationTests.Media.LibraryApi.CreateFolderOkAsync(site.Admin, site.Site, "Docs").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, (await QuickBuilder.IntegrationTests.Media.LibraryApi.UploadToFolderAsync(site.Admin, site.Site, docs.Id).ConfigureAwait(true)).StatusCode);
        var (_, first) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        var host = $"merge-{policy}.test";
        var target = (await TransferApi.ImportAsNewSiteAsync(site.App, site.Admin, first, "Target", hostName: host).ConfigureAwait(true)).GetProperty("siteId").GetGuid();

        // Only the target: a page the archive does not hold, and the target's own about page.
        var extra = (await PagesApi.CreateOkAsync(site.Admin, target, "extra").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var about = (await PagesApi.NodeAsync(site.Admin, target, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var historyBefore = await RetainedAsync(site.Admin, PagesApi.PagePath(target, about)).ConfigureAwait(true);
        var siteBefore = await SiteAsync(site.Admin, target).ConfigureAwait(true);

        // The target's own design draft, which the archive's design meets.
        await PagesApi.OkAsync(await QuickBuilder.IntegrationTests.Editor.EditorApi.SaveCurrentAsync(
            site.Admin, target, QuickBuilder.IntegrationTests.Editor.EditorApi.DefaultWithColor("accent", "Accent")).ConfigureAwait(true)).ConfigureAwait(true);

        // Only the archive: a page and a folder the target does not hold.
        await PagesApi.CreateOkAsync(site.Admin, site.Site, "news").ConfigureAwait(true);
        await QuickBuilder.IntegrationTests.Media.LibraryApi.CreateFolderOkAsync(site.Admin, site.Site, "Brochures").ConfigureAwait(true);
        var (_, second) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(site.Admin, second).ConfigureAwait(true);
        var checkedTransfer = await TransferApi.CheckAsync(site.App, site.Admin, id, "merge", target).ConfigureAwait(true);
        Assert.True(checkedTransfer.GetProperty("state").GetString() == "awaiting_confirmation", checkedTransfer.GetProperty("checkResult").GetRawText());
        var conflicts = checkedTransfer.GetProperty("checkResult").GetProperty("conflicts").EnumerateArray().ToList();

        // FR-022: by address, name, address name and source; never media, which is reused.
        string[] Kinds() => [.. conflicts.Select(c => c.GetProperty("archive").GetProperty("kind").GetString()!).Distinct().Order()];
        Assert.Subset(Kinds().ToHashSet(), new HashSet<string> { "collection", "component", "design", "item", "menu", "page", "redirect", "site" });
        Assert.Contains(conflicts, c => c.GetProperty("targetId").GetGuid() == about);
        Assert.DoesNotContain(conflicts, c => c.GetProperty("archive").GetProperty("kind").GetString() == "media_file");
        Assert.DoesNotContain(conflicts, c => c.GetProperty("archive").GetProperty("label").GetString()!.Contains("news", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(HttpStatusCode.Accepted, (await TransferApi.ConfirmAsync(site.Admin, id, new
        {
            conflictPolicy = policy,
            acceptWarnings = true,
            checkFingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString(),
        }).ConfigureAwait(true)).StatusCode);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);
        var done = await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true);
        Assert.True(done.GetProperty("state").GetString() == "succeeded", $"{done.GetProperty("state")}: {done.GetProperty("failureCode")}");
        Assert.Equal(target, done.GetProperty("siteId").GetGuid());

        // The conflicts listed are the ones resolved.
        var resolved = done.GetProperty("report").GetProperty("resolved").EnumerateArray().ToList();
        foreach (var conflict in conflicts.Where(c => c.GetProperty("archive").GetProperty("kind").GetString() is "page" or "component" or "menu" or "item" or "collection" or "redirect"))
        {
            Assert.Contains(resolved, r => r.GetProperty("archive").GetProperty("id").GetGuid() == conflict.GetProperty("archive").GetProperty("id").GetGuid());
        }

        // FR-027: nothing without a counterpart changes, nothing goes, host names and state stay.
        var paths = await PagesApi.PathsAsync(site.Admin, target).ConfigureAwait(true);
        Assert.Contains("/extra", paths);
        Assert.Contains("/news", paths);
        Assert.Equal(extra, (await PagesApi.NodeAsync(site.Admin, target, "/extra").ConfigureAwait(true)).GetProperty("id").GetGuid());
        var siteAfter = await SiteAsync(site.Admin, target).ConfigureAwait(true);
        Assert.Equal(siteBefore.GetProperty("hostNames").GetRawText(), siteAfter.GetProperty("hostNames").GetRawText());
        Assert.Equal(siteBefore.GetProperty("status").GetString(), siteAfter.GetProperty("status").GetString());

        // Media folders are containers: the same one is reused, a new one added (research ST-10).
        var folders = (await QuickBuilder.IntegrationTests.Media.LibraryApi.FoldersOkAsync(site.Admin, target).ConfigureAwait(true)).Folders.Select(f => f.Name).Order().ToList();
        Assert.Equal(["Brochures", "Docs"], folders);

        // Design: replace takes the archive's draft; skip and rename keep the target's.
        var design = (await QuickBuilder.IntegrationTests.Editor.EditorApi.GetOkAsync(site.Admin, target).ConfigureAwait(true)).GetRawText();
        Assert.Equal(policy != "replace", design.Contains("\"accent\"", StringComparison.Ordinal));

        var historyAfter = await RetainedAsync(site.Admin, PagesApi.PagePath(target, about)).ConfigureAwait(true);
        switch (policy)
        {
            case "replace":
                // FR-024: same page, its old versions kept, the archive's live state as a new version.
                Assert.Equal(about, (await PagesApi.NodeAsync(site.Admin, target, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid());
                Assert.Subset(historyAfter.ToHashSet(), historyBefore.ToHashSet());
                Assert.Equal(historyBefore.Count + 1, historyAfter.Count);
                Assert.DoesNotContain("/about-2", paths);
                break;

            case "skip":
                // FR-025: the target is kept; the menu the archive brings is not added.
                Assert.Equal(historyBefore, historyAfter);
                Assert.DoesNotContain("/about-2", paths);
                break;

            case "rename":
                // FR-026: added beside, under 010's slug rule and the "(imported)" names.
                Assert.Equal(historyBefore, historyAfter);
                Assert.Contains("/about-2", paths);
                Assert.Contains("/about-2/team", paths);
                var components = await PagesApi.OkAsync(await PagesApi.ListComponentsAsync(site.Admin, target).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Contains("Header (imported)", components.GetRawText(), StringComparison.Ordinal);
                break;
        }

        // The merged published content reaches visitors after the commit.
        var visitor = site.App.CreateClientForHost(host);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_merge_from_the_same_site_matches_a_moved_page_by_identity_and_does_not_duplicate_it()
    {
        using var site = await FullSite.CreateAsync(database, "merge-identity").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        var page = await PagesApi.OkAsync(await PagesApi.GetAsync(site.Admin, site.Site, site.About).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(site.Admin, site.Site, site.About, "Company", "company", page.GetProperty("version").GetInt32()).ConfigureAwait(true))
            .ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        var checkedTransfer = await TransferApi.CheckAsync(site.App, site.Admin, id, "merge", site.Site).ConfigureAwait(true);
        var conflict = checkedTransfer.GetProperty("checkResult").GetProperty("conflicts").EnumerateArray()
            .Single(c => c.GetProperty("archive").GetProperty("id").GetGuid() == site.About);
        Assert.Equal(site.About, conflict.GetProperty("targetId").GetGuid());
        Assert.Equal("identity", conflict.GetProperty("matchedBy").GetString(), StringComparer.OrdinalIgnoreCase);

        await PagesApi.OkAsync(await TransferApi.ConfirmAsync(site.Admin, id, new
        {
            conflictPolicy = "skip",
            acceptWarnings = true,
            checkFingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString(),
        }).ConfigureAwait(true), HttpStatusCode.Accepted).ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);
        Assert.Equal("succeeded", (await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true)).GetProperty("state").GetString());

        var paths = await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true);
        Assert.Contains("/company", paths);
        Assert.DoesNotContain("/about", paths);
        Assert.Contains("/company/team", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("/about", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_change_to_the_target_after_the_check_stops_the_merge_with_nothing_written()
    {
        using var site = await FullSite.CreateAsync(database, "merge-changed").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        var checkedTransfer = await TransferApi.CheckAsync(site.App, site.Admin, id, "merge", site.Site).ConfigureAwait(true);

        // A page the check matched changes before the apply.
        var page = await PagesApi.OkAsync(await PagesApi.GetAsync(site.Admin, site.Site, site.About).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(site.Admin, site.Site, site.About, "Company", "company", page.GetProperty("version").GetInt32()).ConfigureAwait(true))
            .ConfigureAwait(true);
        var pathsBefore = await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true);

        await PagesApi.OkAsync(await TransferApi.ConfirmAsync(site.Admin, id, new
        {
            conflictPolicy = "rename",
            acceptWarnings = true,
            checkFingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString(),
        }).ConfigureAwait(true), HttpStatusCode.Accepted).ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);

        var done = await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true);
        Assert.Equal(("failed", "transfer.target_changed"), (done.GetProperty("state").GetString(), done.GetProperty("failureCode").GetString()));
        Assert.Equal(pathsBefore.Order(), (await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true)).Order());
    }

    private static async Task<List<int>> RetainedAsync(HttpClient admin, string pagePath) =>
        [.. (await PagesApi.OkAsync(await PublishingApi.HistoryAsync(admin, pagePath).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("retainedVersions").EnumerateArray().Select(v => v.GetInt32()).Order()];

    private static async Task<JsonElement> SiteAsync(HttpClient admin, Guid siteId) =>
        await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/sites/{siteId}", UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);
}
