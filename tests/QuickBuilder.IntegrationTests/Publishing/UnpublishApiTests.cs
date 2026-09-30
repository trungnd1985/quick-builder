using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Contracts.Events;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 4: a published page is taken down on purpose, never by accident, and follows
/// its address when it moves (012 FR-005, FR-006, FR-011).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UnpublishApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Dictionary<string, Guid> _pages = [];

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "unpublish").ConfigureAwait(false);
        _pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nservices\n  hosting").ConfigureAwait(false);

        foreach (var (address, id) in _pages)
        {
            var path = PagesApi.PagePath(_scenario.SiteA, id);
            await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.HeadingDocument($"Heading of {address}")).ConfigureAwait(false);
            await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
        }
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Unpublishing_takes_the_page_down_at_once_and_keeps_its_draft_children_and_history()
    {
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/services").ConfigureAwait(true)).StatusCode);

        var publication = await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal("draft", publication.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/services").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/services/hosting").ConfigureAwait(true)).StatusCode);

        var draft = await PagesApi.DraftOkAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true);
        Assert.Contains("Heading of /services", draft.GetProperty("document").GetRawText(), StringComparison.Ordinal);

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("unpublish", history.GetProperty("entries")[0].GetProperty("action").GetString());
        Assert.Equal([1], history.GetProperty("retainedVersions").EnumerateArray().Select(n => n.GetInt32()));

        Assert.Contains(await _scenario.OutboxAsync().ConfigureAwait(true), m => m.Type == PageUnpublished.EventType);
    }

    [Fact]
    public async Task Only_a_live_page_can_be_unpublished()
    {
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PublishingApi.UnpublishAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.not_published").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_live_page_cannot_be_deleted_until_it_is_unpublished()
    {
        var hosting = _pages["/services/hosting"];
        var version = (await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/services/hosting").ConfigureAwait(true)).GetProperty("version").GetInt32();

        await PagesApi.RefusedAsync(
            await PagesApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, hosting, version).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.page_published").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/services/hosting").ConfigureAwait(true)).StatusCode);

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, Path("/services/hosting")).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, (await PagesApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, hosting, version).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Publishing_again_serves_the_page_at_its_current_address()
    {
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true)).ConfigureAwait(true);
        await RenameAsync("/services", "offers").ConfigureAwait(true);

        var publication = await PublishingApi.PublishCurrentAsync(_scenario.Admin, Path("/services")).ConfigureAwait(true);

        Assert.Equal(2, publication.GetProperty("liveNumber").GetInt32());
        Assert.EndsWith("/offers", publication.GetProperty("liveUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/offers").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/services").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_live_page_moves_to_its_new_address_at_once_with_its_children()
    {
        await RenameAsync("/services", "offers").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/offers").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/offers/hosting").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/services").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/services/hosting").ConfigureAwait(true)).StatusCode);
    }

    private string Path(string address) => PagesApi.PagePath(_scenario.SiteA, _pages[address]);

    private async Task RenameAsync(string address, string slug)
    {
        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, address).ConfigureAwait(false);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(
                _scenario.Admin, _scenario.SiteA, _pages[address], node.GetProperty("title").GetString()!, slug, node.GetProperty("version").GetInt32())
            .ConfigureAwait(false)).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> VisitAsync(string address) =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetAsync(new Uri(address, UriKind.Relative));
}
