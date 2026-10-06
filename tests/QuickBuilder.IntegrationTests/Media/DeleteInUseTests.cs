using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 5: deleting a file that pages use warns first, and one visitors can see needs
/// the publishing capability (013 FR-015, SC-007, SC-015, research MV-06).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeleteInUseTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "delete-in-use").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task A_used_file_is_refused_with_its_usage_until_the_delete_is_confirmed()
    {
        var file = (await MediaApi.UploadOkAsync(_scenario.Admin, Site)).Id;
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "about", file, publish: false);

        var refused = await UsageScenario.DeleteAsync(_scenario.Admin, Site, file, confirmInUse: false);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("media.in_use", problem.GetProperty("errorCode").GetString());
        Assert.Equal(1, problem.GetProperty("usage").GetProperty("draftPlaces").GetInt32());
        Assert.Contains((await _scenario.RowsAsync()), r => r.Id == file);

        Assert.Equal(HttpStatusCode.NoContent, (await UsageScenario.DeleteAsync(_scenario.Admin, Site, file, confirmInUse: true)).StatusCode);
        Assert.DoesNotContain((await _scenario.RowsAsync()), r => r.Id == file);
    }

    [Fact]
    public async Task An_unused_file_is_deleted_without_confirmation() =>
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await UsageScenario.DeleteAsync(_scenario.Admin, Site, (await MediaApi.UploadOkAsync(_scenario.Admin, Site)).Id, confirmInUse: false)).StatusCode);

    [Fact]
    public async Task An_editor_cannot_delete_their_own_file_while_a_live_page_shows_it_even_confirmed()
    {
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor));
        var file = (await MediaApi.UploadOkAsync(editor, Site)).Id;
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "launch", file, publish: true);

        Assert.False((await UsageScenario.FileAsync(editor, Site, file)).CanDelete);

        foreach (var confirm in new[] { false, true })
        {
            var refused = await UsageScenario.DeleteAsync(editor, Site, file, confirm);
            await LibraryApi.RefusedAsync(refused, HttpStatusCode.Forbidden, "media.in_live_use");
        }

        Assert.Contains((await _scenario.RowsAsync()), r => r.Id == file);
    }

    [Fact]
    public async Task An_editor_may_delete_their_own_file_used_only_in_drafts_or_history_once_confirmed()
    {
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor));
        var draftOnly = (await MediaApi.UploadOkAsync(editor, Site)).Id;
        var historyOnly = (await MediaApi.UploadOkAsync(editor, Site)).Id;
        var replacement = (await MediaApi.UploadOkAsync(editor, Site)).Id;

        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "drafting", draftOnly, publish: false);
        var page = await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "archive", historyOnly, publish: true);
        var path = PagesApi.PagePath(Site, page);
        await Publishing.PublishingApi.SaveAsync(_scenario.Admin, path, Pages.MediaUsageReaderTests.Image(replacement));
        await Publishing.PublishingApi.PublishCurrentAsync(_scenario.Admin, path);

        foreach (var file in new[] { draftOnly, historyOnly })
        {
            Assert.True((await UsageScenario.FileAsync(editor, Site, file)).CanDelete);
            await LibraryApi.RefusedAsync(await UsageScenario.DeleteAsync(editor, Site, file, confirmInUse: false), HttpStatusCode.Conflict, "media.in_use");
            Assert.Equal(HttpStatusCode.NoContent, (await UsageScenario.DeleteAsync(editor, Site, file, confirmInUse: true)).StatusCode);
        }
    }

    [Fact]
    public async Task A_designer_may_delete_a_file_a_live_page_shows_and_the_page_stops_showing_it()
    {
        var (designer, _) = await _scenario.UserAsync((Site, SystemRole.Designer));
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, Site);
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "gallery", file.Id, publish: true);

        var before = await (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, "/gallery")).Content.ReadAsStringAsync();
        Assert.Contains(file.Id.ToString("N"), before, StringComparison.Ordinal);

        Assert.True((await UsageScenario.FileAsync(designer, Site, file.Id)).CanDelete);
        Assert.Equal(HttpStatusCode.NoContent, (await UsageScenario.DeleteAsync(designer, Site, file.Id, confirmInUse: true)).StatusCode);

        // The deletion event refreshes the page; delivered here as the dispatcher would.
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }
        var page = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, "/gallery");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain(file.Id.ToString("N"), await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listings_offer_delete_only_where_the_live_rule_allows()
    {
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor));
        var live = (await MediaApi.UploadOkAsync(editor, Site)).Id;
        var unused = (await MediaApi.UploadOkAsync(editor, Site)).Id;
        await UsageScenario.PageShowingAsync(_scenario.Admin, Site, "shown", live, publish: true);

        var listed = (await LibraryApi.ListOkAsync(editor, Site)).Items.ToDictionary(f => f.Id);

        Assert.False(listed[live].CanDelete);
        Assert.True(listed[live].Usage.InLiveUse);
        Assert.True(listed[unused].CanDelete);
    }
}
