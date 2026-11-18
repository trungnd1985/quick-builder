using System.Net;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// Where a link opens (022 research PV-05): the site's own host name, or, for a site without one,
/// the admin's address; both work, because /_preview answers on every host name.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkAddressTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() =>
        _world = await TemplatesWorld.CreateAsync(database, "preview-address").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_site_with_a_host_name_gets_links_on_it()
    {
        var page = await _world.PageAsync("about").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("About")).ConfigureAwait(true);

        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(true);

        Assert.StartsWith($"https://{CmsScenario.HostA}/_preview/l/", link.Url.Replace("http://", "https://", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await PreviewLinksApi.OpenAsync(_world.App, link.Path, CmsScenario.HostA).ConfigureAwait(true)).Status);
    }

    /// <remarks>
    /// Every site keeps at least one host name (009), so the admin-address fallback of PV-05 is
    /// never used today. What it relies on is tested here: a link opens on any host name, the
    /// admin's included, and still shows its own site's content.
    /// </remarks>
    [Fact]
    public async Task A_link_opens_on_any_host_name_and_shows_its_own_sites_content()
    {
        var page = await _world.PageAsync("anywhere").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("Anywhere")).ConfigureAwait(true);
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(true);

        foreach (var host in new[] { "localhost", CmsScenario.HostB, "unknown.example" })
        {
            var (status, html, _) = await PreviewLinksApi.OpenAsync(_world.App, link.Path, host).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(">Anywhere</h1>", html, StringComparison.Ordinal);
        }
    }
}
