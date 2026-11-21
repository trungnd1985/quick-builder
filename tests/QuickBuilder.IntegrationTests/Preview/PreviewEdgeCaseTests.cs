using System.Net;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// The spec's edge cases not covered elsewhere (022 spec "Edge Cases", quickstart §6): parts never
/// published shown from their drafts and named in the bar (clarification Q2), and a pending
/// schedule named in the bar.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewEdgeCaseTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() =>
        _world = await TemplatesWorld.CreateAsync(database, "preview-edges").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task<string> PreviewAsync(Guid page) =>
        _world.Admin.GetStringAsync(new Uri($"/_preview/sites/{_world.Site}/pages/{page}", UriKind.Relative));

    [Fact]
    public async Task A_never_published_component_is_shown_from_its_draft_and_named_in_the_bar()
    {
        var footer = (await PagesApi.CreateComponentOkAsync(_world.Admin, _world.Site, "Footer").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.ComponentPath(_world.Site, footer), PublishingApi.ComponentDocument("Draft footer")).ConfigureAwait(true);
        var page = await _world.PageAsync("about").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("About", footer)).ConfigureAwait(true);

        var html = await PreviewAsync(page).ConfigureAwait(true);

        Assert.Contains("Draft footer", html, StringComparison.Ordinal);
        Assert.Contains("Shared component “Footer” shown from its draft (never published)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_design_never_published_is_shown_from_its_saved_tokens_and_named_in_the_bar()
    {
        var saved = await EditorApi.SaveCurrentAsync(_world.Admin, _world.Site, EditorApi.DefaultWithColor("previewaccent", "Preview accent", "#ab1234")).ConfigureAwait(true);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync().ConfigureAwait(true));
        var page = await _world.PageAsync("styled").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("Styled")).ConfigureAwait(true);

        var html = await PreviewAsync(page).ConfigureAwait(true);

        Assert.Contains("#ab1234", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The site design shown from its draft (never published)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pending_scheduled_publish_is_named_in_the_bar()
    {
        var page = await _world.PageAsync("launch").ConfigureAwait(true);
        var path = PagesApi.PagePath(_world.Site, page);
        await PublishingApi.SaveAsync(_world.Admin, path, PublishingApi.HeadingDocument("Launch")).ConfigureAwait(true);
        var scheduled = await PublishingApi.ScheduleAsync(
                _world.Admin, path, "publish", DateTimeOffset.UtcNow.AddDays(2), await PublishingApi.DraftVersionAsync(_world.Admin, path).ConfigureAwait(true))
            .ConfigureAwait(true);
        Assert.True(scheduled.IsSuccessStatusCode, await scheduled.Content.ReadAsStringAsync().ConfigureAwait(true));

        Assert.Contains("Scheduled to publish ", await PreviewAsync(page).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bar_never_names_a_part_that_is_live()
    {
        var header = (await PagesApi.CreateComponentOkAsync(_world.Admin, _world.Site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(_world.Site, header);
        await PublishingApi.SaveAsync(_world.Admin, headerPath, PublishingApi.ComponentDocument("Live header")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_world.Admin, headerPath).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, headerPath, PublishingApi.ComponentDocument("Unpublished header change")).ConfigureAwait(true);
        var page = await _world.PageAsync("home-like").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("Page", header)).ConfigureAwait(true);

        var html = await PreviewAsync(page).ConfigureAwait(true);

        Assert.Contains("Live header", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Unpublished header change", html, StringComparison.Ordinal);
        Assert.DoesNotContain("shown from its draft", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await _world.Admin.GetAsync(new Uri($"/_preview/sites/{_world.Site}/pages/{page}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }
}
