using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Rendering.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// User Story 6, SC-002, research PR-05: a publish through one instance is what every other
/// instance serves at its next request, though each keeps its own page cache.
/// </summary>
/// <remarks>
/// Two application instances over one database, each warmed first: a cached page that
/// survived the publish on the second instance — exception E-4 — would be caught here.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class RenderingAcrossInstancesTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Host = "instances.test";

    private QuickBuilderApp _first = null!;
    private QuickBuilderApp _second = null!;
    private HttpClient _admin = null!;
    private string _pagePath = string.Empty;

    public async Task InitializeAsync()
    {
        var connection = await database.CreateDatabaseAsync("render-instances").ConfigureAwait(false);
        _first = new QuickBuilderApp(connection);
        _second = new QuickBuilderApp(connection);
        _ = _first.Services;
        _ = _second.Services;
        _admin = await _first.CreateAdminClientAsync().ConfigureAwait(false);

        var site = await MediaApi.CreateSiteAsync(_admin, "Instances", Host).ConfigureAwait(false);
        var page = (await PagesApi.CreateOkAsync(_admin, site, "news").ConfigureAwait(false)).GetProperty("id").GetGuid();
        _pagePath = PagesApi.PagePath(site, page);
        await PublishingApi.SaveAsync(_admin, _pagePath, PublishingApi.HeadingDocument("First edition")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_admin, _pagePath).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _first.Dispose();
        _second.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_publish_on_one_instance_is_served_by_the_other_at_its_next_request()
    {
        Assert.Contains("First edition", await VisitAsync(_first).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("First edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);

        // The second instance now answers from its cache.
        var renders = _second.Services.GetRequiredService<RenderMetrics>().Renders;
        Assert.Contains("First edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Equal(renders, _second.Services.GetRequiredService<RenderMetrics>().Renders);

        await PublishingApi.SaveAsync(_admin, _pagePath, PublishingApi.HeadingDocument("Second edition")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_admin, _pagePath).ConfigureAwait(true);

        Assert.Contains("Second edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("Second edition", await VisitAsync(_first).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unpublish_on_one_instance_takes_the_page_down_on_the_other()
    {
        Assert.Contains("First edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_admin, _pagePath).ConfigureAwait(true)).ConfigureAwait(true);

        var response = await _second.CreateClientForHost(Host).GetAsync(new Uri("/news", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<string> VisitAsync(QuickBuilderApp app) =>
        app.CreateClientForHost(Host).GetStringAsync(new Uri("/news", UriKind.Relative));
}
