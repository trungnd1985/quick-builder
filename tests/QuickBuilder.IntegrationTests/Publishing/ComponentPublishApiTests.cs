using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 2: shared components are published once and appear on every page that uses
/// them (012 FR-002 to FR-004, research PR-04).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ComponentPublishApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _header;
    private string _headerPath = string.Empty;
    private string _aboutPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "comp-publish").ConfigureAwait(false);
        var about = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(false))["/about"];
        _aboutPath = PagesApi.PagePath(_scenario.SiteA, about);

        _header = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Header").ConfigureAwait(false)).GetProperty("id").GetGuid();
        _headerPath = PagesApi.ComponentPath(_scenario.SiteA, _header);

        await PublishingApi.SaveAsync(_scenario.Admin, _headerPath, PublishingApi.ComponentDocument("Header v1")).ConfigureAwait(false);
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("About", _header)).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_page_cannot_be_published_before_the_components_it_shows()
    {
        var problem = await PagesApi.RefusedAsync(
            await PublishingApi.PublishAsync(_scenario.Admin, _aboutPath,
                await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.unpublished_components").ConfigureAwait(true);

        Assert.Equal(_header, problem.Body.GetProperty("componentIds")[0].GetGuid());
    }

    [Fact]
    public async Task Publishing_a_component_changes_every_page_that_shows_it_without_republishing_them()
    {
        var first = await PublishingApi.PublishCurrentAsync(_scenario.Admin, _headerPath).ConfigureAwait(true);
        Assert.Equal(1, first.GetProperty("liveNumber").GetInt32());
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        Assert.Contains("Header v1", await PageAsync().ConfigureAwait(true), StringComparison.Ordinal);

        await PublishingApi.SaveAsync(_scenario.Admin, _headerPath, PublishingApi.ComponentDocument("Header v2")).ConfigureAwait(true);
        Assert.Contains("Header v1", await PageAsync().ConfigureAwait(true), StringComparison.Ordinal);

        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _headerPath).ConfigureAwait(true);
        var html = await PageAsync().ConfigureAwait(true);

        Assert.Contains("Header v2", html, StringComparison.Ordinal);
        var page = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(true);
        Assert.Equal(1, page.GetProperty("publication").GetProperty("liveNumber").GetInt32());
    }

    [Fact]
    public async Task A_component_that_published_pages_show_cannot_be_unpublished_or_deleted()
    {
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _headerPath).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        var unpublish = await PagesApi.RefusedAsync(
            await PublishingApi.UnpublishAsync(_scenario.Admin, _headerPath).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.component_in_use_by_published_pages").ConfigureAwait(true);
        Assert.Equal(1, unpublish.Body.GetProperty("pageCount").GetInt32());

        // Taking it out of the draft is not enough: the published page still shows it.
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("About")).ConfigureAwait(true);
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(_scenario.Admin, _scenario.SiteA, _header).ConfigureAwait(true)).ConfigureAwait(true))
            .GetProperty("version").GetUInt32();
        await PagesApi.RefusedAsync(
            await PagesApi.DeleteComponentAsync(_scenario.Admin, _scenario.SiteA, _header, version).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.component_in_use_by_published_pages").ConfigureAwait(true);

        // Once the page is published without it, it can go.
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.UnpublishAsync(_scenario.Admin, _headerPath).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PagesApi.DeleteComponentAsync(_scenario.Admin, _scenario.SiteA, _header, version).ConfigureAwait(true)).StatusCode);

        // Its publishing records went with it; its history stayed, marked deleted (FR-017).
        Assert.Equal(0, await _scenario.ReadAsync(db => PagesScenario.Everything<Publication>(db).CountAsync(p => p.SubjectId == _header)).ConfigureAwait(true));
        Assert.Equal(0, await _scenario.ReadAsync(db => PagesScenario.Everything<PublishedVersion>(db).CountAsync(p => p.SubjectId == _header)).ConfigureAwait(true));
        Assert.Contains(
            LogActions.Deleted,
            await _scenario.ReadAsync(db => PagesScenario.Everything<PublishLogEntry>(db).Where(e => e.SubjectId == _header).Select(e => e.Action).ToListAsync()).ConfigureAwait(true));
    }

    [Fact]
    public async Task Each_publish_replaces_the_live_dependencies()
    {
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _headerPath).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        Assert.Equal([_header], await TargetsAsync().ConfigureAwait(true));

        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("About")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        Assert.Empty(await TargetsAsync().ConfigureAwait(true));
    }

    private Task<List<Guid>> TargetsAsync() =>
        _scenario.ReadAsync(db => PagesScenario.Everything<LiveDependency>(db)
            .Where(d => d.SubjectKind == SubjectKinds.Page)
            .Select(d => d.TargetId)
            .ToListAsync());

    private Task<string> PageAsync() =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri("/about", UriKind.Relative));
}
