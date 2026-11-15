using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 3: every page and component keeps its history; a retained version can be
/// published again or copied into the draft (012 FR-013 to FR-016, research PR-15).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RollbackApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _about;
    private string _aboutPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "rollback").ConfigureAwait(false);
        _about = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(false))["/about"];
        _aboutPath = PagesApi.PagePath(_scenario.SiteA, _about);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Rolling_a_page_back_publishes_the_old_content_as_a_new_version_and_leaves_the_draft()
    {
        await PublishHeadingsAsync(_aboutPath, "One", "Two", "Three").ConfigureAwait(true);

        var rolledBack = await PagesApi.OkAsync(await PublishingApi.RollbackAsync(_scenario.Admin, _aboutPath, 1).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(4, rolledBack.GetProperty("liveNumber").GetInt32());
        Assert.True(rolledBack.GetProperty("hasUnpublishedChanges").GetBoolean());
        var html = await PageAsync().ConfigureAwait(true);
        Assert.Contains(">One<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Three<", html, StringComparison.Ordinal);

        var draft = await PagesApi.DraftOkAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        Assert.Contains("Three", draft.GetProperty("document").GetRawText(), StringComparison.Ordinal);

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal([4, 3, 2, 1], history.GetProperty("retainedVersions").EnumerateArray().Select(n => n.GetInt32()));
        var entries = history.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["rollback", "publish", "publish", "publish"], entries.Select(e => e.GetProperty("action").GetString()));
        Assert.Equal(1, entries[0].GetProperty("restoredFromNumber").GetInt32());
        Assert.Equal(4, entries[0].GetProperty("versionNumber").GetInt32());
        Assert.True(entries[0].GetProperty("subjectExists").GetBoolean());
        Assert.Equal(JsonValueKind.String, entries[0].GetProperty("actor").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task A_version_reads_back_with_its_document_and_title()
    {
        await PublishHeadingsAsync(_aboutPath, "One").ConfigureAwait(true);

        var version = await PagesApi.OkAsync(await PublishingApi.VersionAsync(_scenario.Admin, _aboutPath, 1).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(1, version.GetProperty("number").GetInt32());
        Assert.Equal("publish", version.GetProperty("kind").GetString());
        Assert.EndsWith("about", version.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One", version.GetProperty("document").GetRawText(), StringComparison.Ordinal);

        await PagesApi.RefusedAsync(await PublishingApi.VersionAsync(_scenario.Admin, _aboutPath, 9).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.version_not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PublishingApi.RollbackAsync(_scenario.Admin, _aboutPath, 9).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.version_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Copying_a_version_to_the_draft_uses_the_draft_rules_and_changes_nothing_public()
    {
        await PublishHeadingsAsync(_aboutPath, "One", "Two").ConfigureAwait(true);
        var before = await PageAsync().ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PublishingApi.CopyToDraftAsync(_scenario.Admin, _aboutPath, 1, draftVersion + 1).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.out_of_date").ConfigureAwait(true);

        var copied = await PagesApi.OkAsync(await PublishingApi.CopyToDraftAsync(_scenario.Admin, _aboutPath, 1, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Contains("One", copied.GetProperty("document").GetRawText(), StringComparison.Ordinal);
        Assert.NotEqual(draftVersion, copied.GetProperty("version").GetUInt32());
        Assert.Equal(before, await PageAsync().ConfigureAwait(true));

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        var copy = history.GetProperty("entries")[0];
        Assert.Equal("copied_to_draft", copy.GetProperty("action").GetString());
        Assert.Equal(1, copy.GetProperty("versionNumber").GetInt32());
        Assert.True(history.GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean());
    }

    [Fact]
    public async Task A_version_whose_components_are_gone_or_not_live_cannot_be_rolled_back_to()
    {
        var header = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(_scenario.SiteA, header);
        await PublishingApi.SaveAsync(_scenario.Admin, headerPath, PublishingApi.ComponentDocument("Header")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, headerPath).ConfigureAwait(true);

        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("With header", header)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PublishHeadingsAsync(_aboutPath, "Without").ConfigureAwait(true);

        // Still there but taken down: publish it first.
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, headerPath).ConfigureAwait(true)).ConfigureAwait(true);
        var unpublished = await PagesApi.RefusedAsync(
            await PublishingApi.RollbackAsync(_scenario.Admin, _aboutPath, 1).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.unpublished_components").ConfigureAwait(true);
        Assert.Equal(header, unpublished.Body.GetProperty("componentIds")[0].GetGuid());

        // Gone altogether: that version cannot come back.
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(_scenario.Admin, _scenario.SiteA, header).ConfigureAwait(true)).ConfigureAwait(true))
            .GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.DeleteComponentAsync(_scenario.Admin, _scenario.SiteA, header, version).ConfigureAwait(true)).StatusCode);
        var missing = await PagesApi.RefusedAsync(
            await PublishingApi.RollbackAsync(_scenario.Admin, _aboutPath, 1).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.version_missing_components").ConfigureAwait(true);
        Assert.Equal(header, missing.Body.GetProperty("componentIds")[0].GetGuid());
    }

    [Fact]
    public async Task Fifty_five_publishes_keep_the_fifty_newest_versions()
    {
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("Same")).ConfigureAwait(true);
        for (var i = 0; i < 55; i++)
        {
            await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        }

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        var retained = history.GetProperty("retainedVersions").EnumerateArray().Select(n => n.GetInt32()).ToList();

        Assert.Equal(50, retained.Count);
        Assert.Equal(55, retained[0]);
        Assert.Equal(6, retained[^1]);
        Assert.Equal(55, history.GetProperty("entries").GetArrayLength());
        await PagesApi.RefusedAsync(await PublishingApi.RollbackAsync(_scenario.Admin, _aboutPath, 5).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.version_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_component_has_the_same_history_versions_and_copy()
    {
        var footer = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Footer").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = PagesApi.ComponentPath(_scenario.SiteA, footer);
        await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.ComponentDocument("Footer one")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.ComponentDocument("Footer two")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(true);

        var rolledBack = await PagesApi.OkAsync(await PublishingApi.RollbackAsync(_scenario.Admin, path, 1).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(3, rolledBack.GetProperty("liveNumber").GetInt32());

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, path).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(3, history.GetProperty("retainedVersions").GetArrayLength());
        Assert.Equal("component", history.GetProperty("entries")[0].GetProperty("subjectKind").GetString());

        var version = await PagesApi.OkAsync(await PublishingApi.VersionAsync(_scenario.Admin, path, 1).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("Footer one", version.GetProperty("document").GetRawText(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("title").ValueKind);

        var editor = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, path).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.HistoryAsync(editor, path).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.VersionAsync(editor, path, 1).ConfigureAwait(true)).StatusCode);
        await PagesApi.RefusedAsync(
            await PublishingApi.CopyToDraftAsync(editor, path, 1, draftVersion).ConfigureAwait(true),
            HttpStatusCode.Forbidden,
            "identity.forbidden").ConfigureAwait(true);

        var copied = await PagesApi.OkAsync(await PublishingApi.CopyToDraftAsync(_scenario.Admin, path, 1, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("Footer one", copied.GetProperty("document").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editors_copy_page_versions_but_cannot_roll_back()
    {
        await PublishHeadingsAsync(_aboutPath, "One").ConfigureAwait(true);
        var editor = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);

        await PagesApi.RefusedAsync(await PublishingApi.RollbackAsync(editor, _aboutPath, 1).ConfigureAwait(true), HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(true);

        var draftVersion = await PublishingApi.DraftVersionAsync(editor, _aboutPath).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.CopyToDraftAsync(editor, _aboutPath, 1, draftVersion).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_subject_of_another_site_or_none_at_all_is_not_found()
    {
        var elsewhere = PagesApi.PagePath(_scenario.SiteA, Guid.NewGuid());

        await PagesApi.RefusedAsync(await PublishingApi.HistoryAsync(_scenario.Admin, elsewhere).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PublishingApi.CopyToDraftAsync(_scenario.Admin, elsewhere, 1, 1).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.not_found").ConfigureAwait(true);
        Assert.Equal(0, await _scenario.ReadAsync(db => PagesScenario.Everything<PublishLogEntry>(db).CountAsync(e => e.SubjectId == _about)).ConfigureAwait(true));
    }

    private async Task PublishHeadingsAsync(string path, params string[] headings)
    {
        foreach (var heading in headings)
        {
            await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.HeadingDocument(heading)).ConfigureAwait(false);
            await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
        }
    }

    private Task<string> PageAsync() =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri("/about", UriKind.Relative));
}
