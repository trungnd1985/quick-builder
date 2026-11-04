using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Damaged, newer, unsafe and invalid archives are refused at the check with a stated reason,
/// and nothing on the installation changes (020 FR-012 … FR-015, SC-003).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ArchiveRefusalTests(DatabaseFixture database)
{
    public static TheoryData<string, string> Refusals() => new()
    {
        { "truncated", "archive.unreadable" },
        { "byte-changed", "archive.integrity" },
        { "unlisted-entry", "archive.integrity" },
        { "unsafe-path", "archive.unsafe_path" },
        { "entry-removed", "archive.integrity" },
        { "format-2", "transfer.format_too_new" },
        { "newer-major", "transfer.release_too_new" },
        { "page-too-new", "transfer.document_too_new" },
        { "invalid-block", "transfer.document_invalid" },
        { "invalid-schema", "transfer.document_invalid" },
        { "not-json-line", "transfer.document_invalid" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_bad_archive_is_refused_at_the_check_and_nothing_changes(string mutation, string code)
    {
        using var scenario = await PagesScenario.CreateAsync(database, $"refuse-{mutation}").ConfigureAwait(true);
        var page = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, page), PublishingApi.HeadingDocument("About")).ConfigureAwait(true);
        await MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        await QuickBuilder.IntegrationTests.Cms.CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        var bad = Mutate(mutation, archive);
        var before = await SitesAsync(scenario.App).ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(scenario.Admin, bad).ConfigureAwait(true);
        var transfer = await TransferApi.CheckAsync(scenario.App, scenario.Admin, id, "new_site").ConfigureAwait(true);

        Assert.Equal("refused", transfer.GetProperty("state").GetString());
        var problems = transfer.GetProperty("checkResult").GetProperty("problems").EnumerateArray().ToList();
        Assert.Contains(problems, p => p.GetProperty("severity").GetString() == "error" && p.GetProperty("code").GetString() == code);
        Assert.All(problems.Where(p => p.GetProperty("severity").GetString() == "error"), p => Assert.False(string.IsNullOrEmpty(p.GetProperty("detail").GetString())));
        await PagesApi.RefusedAsync(
            await TransferApi.ConfirmAsync(scenario.Admin, id, new { newSiteName = "X", newSiteHostName = "x.test", acceptWarnings = true }).ConfigureAwait(true),
            System.Net.HttpStatusCode.Conflict, "transfer.not_confirmable").ConfigureAwait(true);
        Assert.Equal(before, await SitesAsync(scenario.App).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_media_file_whose_content_is_not_its_type_is_a_warning_and_is_left_out_when_accepted()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "refuse-media").ConfigureAwait(true);
        await MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        var bad = ArchiveMutator.ReplaceEntry(archive, ArchiveMutator.FirstMedia(archive), Encoding.UTF8.GetBytes("<script>not an image</script>"));

        var id = await TransferApi.UploadAsync(scenario.Admin, bad).ConfigureAwait(true);
        var transfer = await TransferApi.CheckAsync(scenario.App, scenario.Admin, id, "new_site").ConfigureAwait(true);

        Assert.Equal("awaiting_confirmation", transfer.GetProperty("state").GetString());
        Assert.Contains(transfer.GetProperty("checkResult").GetProperty("problems").EnumerateArray(),
            p => p.GetProperty("severity").GetString() == "warning" && p.GetProperty("code").GetString() == "transfer.media_refused");

        await PagesApi.OkAsync(await TransferApi.ConfirmAsync(scenario.Admin, id, new
        {
            newSiteName = "Copy", newSiteHostName = "copy.test", acceptWarnings = true, checkFingerprint = transfer.GetProperty("checkFingerprint").GetString(),
        }).ConfigureAwait(true), System.Net.HttpStatusCode.Accepted).ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(scenario.App).ConfigureAwait(true);

        var done = await TransferApi.GetOkAsync(scenario.Admin, id).ConfigureAwait(true);
        Assert.Equal("succeeded", done.GetProperty("state").GetString());
        Assert.Contains(done.GetProperty("report").GetProperty("leftOut").EnumerateArray(), l => l.GetProperty("reason").GetString() == "media_refused");
        var files = await MediaApi.ListOkAsync(scenario.Admin, done.GetProperty("siteId").GetGuid()).ConfigureAwait(true);
        Assert.Empty(files.Items);
    }

    private static byte[] Mutate(string mutation, byte[] archive) => mutation switch
    {
        "truncated" => ArchiveMutator.Truncate(archive),
        "byte-changed" => ArchiveMutator.FlipByteIn(archive, "parts/pages/tree.jsonl"),
        "unlisted-entry" => ArchiveMutator.AddUnlisted(archive, "parts/pages/extra.jsonl"),
        "unsafe-path" => ArchiveMutator.AddUnlisted(archive, "../evil"),
        "entry-removed" => ArchiveMutator.Remove(archive, "parts/pages/tree.jsonl"),
        "format-2" => ArchiveMutator.SetManifest(archive, m => m["format"] = 2),
        "newer-major" => ArchiveMutator.SetManifest(archive, m => m["release"] = "99.0.0"),
        "page-too-new" => ArchiveMutator.EditLines(archive, "parts/pages/page-drafts.jsonl", d =>
        {
            d["document"]!["schemaVersion"] = 99;
            return d;
        }),
        "invalid-block" => ArchiveMutator.EditLines(archive, "parts/pages/page-drafts.jsonl", d =>
        {
            d["document"]!["root"]!["children"] = new JsonArray(new JsonObject { ["id"] = "x", ["type"] = "no-such-block", ["props"] = new JsonObject(), ["children"] = new JsonArray() });
            return d;
        }),
        "invalid-schema" => ArchiveMutator.EditLines(archive, "parts/cms/collections.jsonl", c =>
        {
            c["schema"] = new JsonObject { ["schemaVersion"] = 1, ["fields"] = "not a list" };
            return c;
        }),
        "not-json-line" => ArchiveMutator.ReplaceEntry(archive, "parts/pages/tree.jsonl", Encoding.UTF8.GetBytes("{ this is not json\n")),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
    };

    private static async Task<int> SitesAsync(QuickBuilderApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await QuickBuilder.Shared.Infrastructure.MultiSite.CrossSiteQuery
            .AcrossAllSites(scope.ServiceProvider.GetRequiredService<SitesDbContext>().Sites, "Test: count every site.")
            .CountAsync().ConfigureAwait(false);
    }
}
