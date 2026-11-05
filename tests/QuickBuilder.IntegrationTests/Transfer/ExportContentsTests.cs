using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using QuickBuilder.Sites.Features.Transfer.Engine;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// An export holds the whole site, and nothing it must not (020 FR-001 … FR-004, FR-009;
/// contracts/archive-parts.md §3).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExportContentsTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_export_holds_every_kind_of_content_and_its_summary_counts_match_the_site()
    {
        using var site = await FullSite.CreateAsync(database, "export-contents").ConfigureAwait(true);

        var (id, bytes) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        await using var archive = await TransferApi.OpenAsync(bytes).ConfigureAwait(true);

        // The manifest names its source, release, parts and contract versions (FR-004).
        Assert.Equal(1, archive.Manifest.Format);
        Assert.Equal(site.Site, archive.Manifest.Source.SiteId);
        Assert.Equal(["sites", "media", "design", "cms", "pages"], archive.Manifest.Parts.Select(p => p.Name));
        Assert.Equal(1, archive.Manifest.ContractVersion("page-schema"));
        Assert.True((await archive.VerifyAllAsync(null, CancellationToken.None).ConfigureAwait(true)).IsSuccess);

        foreach (var (kind, expected) in FullSite.ExpectedCounts)
        {
            Assert.True(archive.Manifest.Counts.TryGetValue(kind, out var count) && count == expected, $"{kind}: expected {expected}, got {count}");
        }

        var transfer = await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true);
        Assert.Equal(bytes.Length, transfer.GetProperty("archiveBytes").GetInt64());
        Assert.True(transfer.GetProperty("downloadable").GetBoolean());
        Assert.Equal(FullSite.ExpectedCounts["page"], transfer.GetProperty("summary").GetProperty("counts").GetProperty("page").GetInt64());
    }

    [Fact]
    public async Task Drafts_live_versions_retained_versions_schedules_and_templates_are_carried()
    {
        using var site = await FullSite.CreateAsync(database, "export-history").ConfigureAwait(true);

        var (_, bytes) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        await using var archive = await TransferApi.OpenAsync(bytes).ConfigureAwait(true);

        // FR-002: the about page's draft is ahead of its two published versions.
        var drafts = await TransferApi.LinesAsync(archive, "pages", "page-drafts").ConfigureAwait(true);
        Assert.Contains("About Acme (draft)", drafts.Single(d => d.GetProperty("subjectId").GetGuid() == site.About).GetRawText(), StringComparison.Ordinal);
        var versions = await TransferApi.LinesAsync(archive, "pages", "versions").ConfigureAwait(true);
        Assert.Equal([1, 2], versions.Where(v => v.GetProperty("subjectId").GetGuid() == site.About).Select(v => v.GetProperty("number").GetInt32()));
        var publications = await TransferApi.LinesAsync(archive, "pages", "publications").ConfigureAwait(true);
        Assert.Equal(2, publications.Single(p => p.GetProperty("subjectId").GetGuid() == site.About).GetProperty("liveNumber").GetInt32());
        Assert.Contains(publications, p => p.GetProperty("subjectKind").GetString() == "menu" && p.GetProperty("subjectId").GetGuid() == site.Menu);

        var schedules = await TransferApi.LinesAsync(archive, "pages", "schedules").ConfigureAwait(true);
        Assert.Equal(site.Team, schedules.Single().GetProperty("subjectId").GetGuid());

        var tree = await TransferApi.LinesAsync(archive, "pages", "tree").ConfigureAwait(true);
        Assert.Equal(site.About, tree.Single(p => p.GetProperty("id").GetGuid() == site.Team).GetProperty("parentId").GetGuid());
        Assert.Equal(site.World.Posts, tree.Single(p => p.GetProperty("id").GetGuid() == site.Template).GetProperty("templateCollectionId").GetGuid());

        // Items: the post's draft is ahead of its live values, and both name the person.
        var items = await TransferApi.LinesAsync(archive, "cms", "items").ConfigureAwait(true);
        var post = items.Single(i => i.GetProperty("id").GetGuid() == site.Post);
        Assert.Contains("Hello, again", post.GetProperty("draft").GetRawText(), StringComparison.Ordinal);
        Assert.Equal(site.Person.ToString(), post.GetProperty("published").GetProperty("values").GetProperty("f9").GetString());
        Assert.NotEmpty(await TransferApi.LinesAsync(archive, "cms", "item-versions").ConfigureAwait(true));
        Assert.NotEmpty(await TransferApi.LinesAsync(archive, "cms", "schema-changes").ConfigureAwait(true));

        var redirects = await TransferApi.LinesAsync(archive, "pages", "redirects").ConfigureAwait(true);
        Assert.Equal(site.About, redirects.Single().GetProperty("targetPageId").GetGuid());

        var design = await TransferApi.LinesAsync(archive, "design", "design").ConfigureAwait(true);
        Assert.Equal(1, design.Single().GetProperty("liveNumber").GetInt32());
        Assert.Single(await TransferApi.LinesAsync(archive, "design", "design-versions").ConfigureAwait(true));
    }

    [Fact]
    public async Task Media_bytes_travel_by_file_id_and_a_file_whose_bytes_are_gone_is_reported_not_fatal()
    {
        using var site = await FullSite.CreateAsync(database, "export-media").ConfigureAwait(true);

        var (id, bytes) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        await using var archive = await TransferApi.OpenAsync(bytes).ConfigureAwait(true);

        var files = await TransferApi.LinesAsync(archive, "media", "files").ConfigureAwait(true);
        Assert.Equal(3, files.Count);
        var missing = files.Single(f => f.GetProperty("id").GetGuid() == site.MissingImage);
        Assert.Equal("missing", missing.GetProperty("bytes").GetString());
        Assert.False(archive.HasMedia(site.MissingImage));
        Assert.True(archive.HasMedia(site.World.Items.Image));
        Assert.Equal(archive.MediaEntry(site.World.Items.Image)!.Sha256, files.Single(f => f.GetProperty("id").GetGuid() == site.World.Items.Image).GetProperty("sha256").GetString());

        // FR-009: listed in the manifest and the summary, and the export still succeeded.
        Assert.Contains(archive.Manifest.Problems, p => p.Code == "transfer.media_missing" && p.Id == site.MissingImage);
        var summary = (await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true)).GetProperty("summary");
        Assert.Contains(summary.GetProperty("problems").EnumerateArray(), p => p.GetProperty("id").GetGuid() == site.MissingImage);
    }

    [Fact]
    public async Task Nothing_the_archive_must_not_carry_is_in_it()
    {
        using var site = await FullSite.CreateAsync(database, "export-excluded").ConfigureAwait(true);

        var (_, bytes) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        await using var archive = await TransferApi.OpenAsync(bytes).ConfigureAwait(true);

        // FR-003: entries are exactly the parts' own (archive-parts.md §3); no derived rows,
        // no submissions, sessions, takeovers, accounts or logs.
        var names = archive.Manifest.Entries.Select(e => e.Name).Where(n => n.StartsWith("parts/", StringComparison.Ordinal)).Order().ToList();
        Assert.Equal(
            [
                "parts/cms/collections.jsonl", "parts/cms/item-history.jsonl", "parts/cms/item-schedules.jsonl",
                "parts/cms/item-versions.jsonl", "parts/cms/items.jsonl", "parts/cms/schema-changes.jsonl",
                "parts/design/design-versions.jsonl", "parts/design/design.jsonl",
                "parts/media/files.jsonl", "parts/media/folders.jsonl",
                "parts/pages/components.jsonl", "parts/pages/menus.jsonl", "parts/pages/page-drafts.jsonl",
                "parts/pages/publications.jsonl", "parts/pages/redirects.jsonl", "parts/pages/schedules.jsonl",
                "parts/pages/tree.jsonl", "parts/pages/versions.jsonl",
                "parts/sites/host-names.jsonl", "parts/sites/search.jsonl", "parts/sites/settings.jsonl",
            ],
            names);

        // Authors carry a display name and nothing else (research ST-14): no email anywhere.
        var authors = await TransferApi.LinesAsync(archive, "authors").ConfigureAwait(true);
        Assert.NotEmpty(authors);
        Assert.All(authors, a => Assert.Equal(["id", "displayName"], a.EnumerateObject().Select(p => p.Name)));
        foreach (var entry in names)
        {
            var parts = entry.Split('/');
            foreach (var line in await TransferApi.LinesAsync(archive, parts[1], parts[2][..^".jsonl".Length]).ConfigureAwait(true))
            {
                Assert.DoesNotContain("@", line.GetRawText(), StringComparison.Ordinal);
            }
        }
    }
}
