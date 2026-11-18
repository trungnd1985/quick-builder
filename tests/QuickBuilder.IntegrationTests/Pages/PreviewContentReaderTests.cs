using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using Xunit;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Drafts and retained versions for previews (022 research PV-02, module-surface.md §2.1),
/// through Pages' own read-only preview context.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewContentReaderTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Dictionary<string, Guid> _pages = null!;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "preview-reader").ConfigureAwait(false);
        _pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nabout\nnews").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private string About => PagesApi.PagePath(_scenario.SiteA, _pages["/about"]);

    private async Task<T> WithReaderAsync<T>(Func<IPreviewContentReader, Task<T>> work, Guid? site = null)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(site ?? _scenario.SiteA);
        return await work(scope.ServiceProvider.GetRequiredService<IPreviewContentReader>()).ConfigureAwait(false);
    }

    [Fact]
    public async Task A_draft_comes_with_its_title_address_saver_and_live_state()
    {
        await PublishingApi.SaveAsync(_scenario.Admin, About, PublishingApi.HeadingDocument("Draft heading")).ConfigureAwait(true);

        var draft = await WithReaderAsync(r => r.GetPageDraftAsync(_scenario.SiteA, _pages["/about"])).ConfigureAwait(true);

        Assert.NotNull(draft);
        Assert.Equal("/about", draft.Path);
        Assert.False(draft.IsTemplate);
        Assert.False(draft.IsLive);
        Assert.Null(draft.PendingScheduleAtUtc);
        Assert.Contains("Draft heading", draft.Document.GetRawText(), StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, draft.SavedBy);
        Assert.Equal(1, draft.SchemaVersion);
    }

    [Fact]
    public async Task A_pending_scheduled_publish_and_the_live_state_are_reported()
    {
        await PublishingApi.SaveAsync(_scenario.Admin, About, PublishingApi.HeadingDocument("One")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, About).ConfigureAwait(true);
        var due = DateTimeOffset.UtcNow.AddDays(2);
        var scheduled = await PublishingApi.ScheduleAsync(
                _scenario.Admin, About, "publish", due, await PublishingApi.DraftVersionAsync(_scenario.Admin, About).ConfigureAwait(true))
            .ConfigureAwait(true);
        Assert.True(scheduled.IsSuccessStatusCode, await scheduled.Content.ReadAsStringAsync().ConfigureAwait(true));

        var draft = await WithReaderAsync(r => r.GetPageDraftAsync(_scenario.SiteA, _pages["/about"])).ConfigureAwait(true);

        Assert.True(draft!.IsLive);
        Assert.NotNull(draft.PendingScheduleAtUtc);
        Assert.Equal(due.ToUnixTimeSeconds(), draft.PendingScheduleAtUtc!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Each_retained_version_is_read_and_an_unknown_one_is_absent()
    {
        await PublishingApi.SaveAsync(_scenario.Admin, About, PublishingApi.HeadingDocument("First")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, About).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, About, PublishingApi.HeadingDocument("Second")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, About).ConfigureAwait(true);

        var first = await WithReaderAsync(r => r.GetPageVersionAsync(_scenario.SiteA, _pages["/about"], 1)).ConfigureAwait(true);
        var second = await WithReaderAsync(r => r.GetPageVersionAsync(_scenario.SiteA, _pages["/about"], 2)).ConfigureAwait(true);
        var missing = await WithReaderAsync(r => r.GetPageVersionAsync(_scenario.SiteA, _pages["/about"], 3)).ConfigureAwait(true);

        Assert.Contains("First", first!.Document.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(1, first.Number);
        Assert.Equal("/about", first.Path);
        Assert.Contains("Second", second!.Document.GetRawText(), StringComparison.Ordinal);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Only_never_published_components_come_from_their_drafts()
    {
        var published = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var draftOnly = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Footer").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(_scenario.SiteA, published);
        await PublishingApi.SaveAsync(_scenario.Admin, headerPath, PublishingApi.ComponentDocument("Live header")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, headerPath).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, PagesApi.ComponentPath(_scenario.SiteA, draftOnly), PublishingApi.ComponentDocument("Draft footer"))
            .ConfigureAwait(true);

        var drafts = await WithReaderAsync(r => r.GetUnpublishedComponentDraftsAsync(_scenario.SiteA, [published, draftOnly])).ConfigureAwait(true);

        Assert.Equal([draftOnly], drafts.Keys);
        Assert.Equal("Footer", drafts[draftOnly].Name);
        Assert.Contains("Draft footer", drafts[draftOnly].Document.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_in_the_trash_or_of_another_site_is_absent()
    {
        var news = _pages["/news"];
        var page = await PagesApi.OkAsync(await PagesApi.GetAsync(_scenario.Admin, _scenario.SiteA, news).ConfigureAwait(true)).ConfigureAwait(true);
        var deleted = await PagesApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, news, page.GetProperty("version").GetInt32()).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Null(await WithReaderAsync(r => r.GetPageDraftAsync(_scenario.SiteA, news)).ConfigureAwait(true));
        Assert.Null(await WithReaderAsync(r => r.GetPageDraftAsync(_scenario.SiteB, _pages["/about"]), _scenario.SiteB).ConfigureAwait(true));
        Assert.Null(await WithReaderAsync(r => r.GetPageDraftAsync(_scenario.SiteA, _pages["/about"]), _scenario.SiteB).ConfigureAwait(true));
    }

    [Fact]
    public async Task Titles_are_read_for_pages_of_the_site_only()
    {
        var titles = await WithReaderAsync(r => r.GetTitlesAsync(_scenario.SiteA, [_pages["/about"], Guid.NewGuid()])).ConfigureAwait(true);

        Assert.Equal([_pages["/about"]], titles.Keys);
    }

    [Fact]
    public async Task The_preview_context_cannot_save()
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(_scenario.SiteA);
        var context = scope.ServiceProvider.GetRequiredService<PreviewReadDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync()).ConfigureAwait(true);
    }
}
