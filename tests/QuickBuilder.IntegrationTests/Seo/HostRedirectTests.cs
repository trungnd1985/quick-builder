using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Sites;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 6: an Administrator can make the primary host name the only one that serves the
/// site; media and the stylesheet stay on every host name (017 FR-014; research SE-07).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HostRedirectTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Alias = "www.pages-a.test";

    private PagesScenario _scenario = null!;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-hosts").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, (await SitesApi.AddHostNameAsync(_scenario.Admin, _scenario.SiteA, Alias).ConfigureAwait(false)).StatusCode);
        var about = PagesApi.PagePath(_scenario.SiteA, (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(false))["/about"]);
        await PublishingApi.SaveAsync(_scenario.Admin, about, PublishingApi.HeadingDocument("About")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, about).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> VisitAsync(string host, string address) => RedirectsApi.VisitAsync(_scenario.App, host, address);

    private Task<System.Text.Json.JsonElement> SetAsync(bool on) => SeoApi.SetSearchVisibilityAsync(_scenario.Admin, _scenario.SiteA, redirectOtherHostNames: on);

    [Fact]
    public async Task With_the_setting_off_another_host_name_serves_the_page_naming_the_primary_one()
    {
        await SetAsync(false);

        var response = await VisitAsync(Alias, "/about");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"https://{PagesScenario.HostA}/about", SeoApi.Head(await response.Content.ReadAsStringAsync(), "rel", "canonical", "href"));
    }

    [Theory]
    [InlineData("/about?x=1")]
    [InlineData("/no-such-page")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    public async Task With_the_setting_on_every_public_address_goes_to_the_primary_host_name(string address)
    {
        await SetAsync(true);

        var response = await VisitAsync(Alias, address);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"https://{PagesScenario.HostA}{address}", response.Headers.Location?.OriginalString);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync(PagesScenario.HostA, "/about")).StatusCode);
    }

    [Theory]
    [InlineData("/_media/0192f5a0-0000-7000-8000-000000000000.png")]
    [InlineData("/_assets/blocks.css")]
    public async Task Media_and_the_stylesheet_answer_on_every_host_name(string address)
    {
        await SetAsync(true);

        Assert.NotEqual(HttpStatusCode.MovedPermanently, (await VisitAsync(Alias, address)).StatusCode);
    }

    [Fact]
    public async Task A_new_site_redirects_by_default_and_an_upgraded_one_does_not()
    {
        var created = await _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<bool>("SELECT redirect_other_hosts AS \"Value\" FROM sites.sites WHERE id = {0}", _scenario.SiteA)
            .SingleAsync());
        var column = await _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT column_default AS \"Value\" FROM information_schema.columns WHERE table_schema = 'sites' AND table_name = 'sites' AND column_name = 'redirect_other_hosts'")
            .SingleAsync());

        Assert.True(created);
        Assert.Equal("false", column);
    }

    [Fact]
    public async Task Only_administrators_change_it()
    {
        var designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer));
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA);

        var response = await SeoApi.PutSearchVisibilityAsync(designer, _scenario.SiteA, version, redirectOtherHostNames: false);

        await PagesApi.RefusedAsync(response, HttpStatusCode.Forbidden, "identity.forbidden");
    }
}
