using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 2: visitors are redirected in the order of FR-016, with the query rules of
/// public-seo.md §2 (017 FR-016 to FR-018, FR-017a, FR-021; research SE-10).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectMatchingTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _about;
    private Guid _offers;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-redirect-matching").ConfigureAwait(false);
        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nabout\noffers\nlive").ConfigureAwait(false);
        _about = pages["/about"];
        _offers = pages["/offers"];
        foreach (var page in new[] { pages["/"], _about, _offers, pages["/live"] })
        {
            var path = PagesApi.PagePath(_scenario.SiteA, page);
            await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.HeadingDocument("Page")).ConfigureAwait(false);
            await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
        }
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> VisitAsync(string address, string host = PagesScenario.HostA) =>
        RedirectsApi.VisitAsync(_scenario.App, host, address);

    private Task<System.Text.Json.JsonElement> CreateAsync(string source, object target, int status = 301, string match = "exact") =>
        RedirectsApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, source, target, status, match);

    [Fact]
    public async Task A_permanent_redirect_sends_to_its_page_and_may_be_cached_briefly()
    {
        await CreateAsync("/about-us.html", RedirectsApi.ToPage(_about)).ConfigureAwait(true);

        var response = await VisitAsync("/about-us.html").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/about", response.Headers.Location?.OriginalString);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_temporary_redirect_is_not_cached_and_carries_the_query()
    {
        await CreateAsync("/sale", RedirectsApi.ToPath("/offers/autumn"), 302).ConfigureAwait(true);

        var response = await VisitAsync("/sale?utm=x").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/offers/autumn?utm=x", response.Headers.Location?.OriginalString);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_target_with_its_own_query_keeps_it_and_drops_the_request_s()
    {
        await CreateAsync("/promo", RedirectsApi.ToPath("/offers?from=promo")).ConfigureAwait(true);

        var response = await VisitAsync("/promo?utm=x").ConfigureAwait(true);

        Assert.Equal("/offers?from=promo", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Live_content_wins_over_a_redirect_without_a_query()
    {
        await CreateAsync("/live", RedirectsApi.ToPath("/elsewhere")).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/live").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_query_redirect_wins_over_live_content_at_its_path()
    {
        await CreateAsync("/?p=12", RedirectsApi.ToPage(_about)).ConfigureAwait(true);

        var redirected = await VisitAsync("/?p=12").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.MovedPermanently, redirected.StatusCode);
        Assert.Equal("/about", redirected.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/?p=13").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_query_source_matches_its_exact_parameters_in_any_order_only()
    {
        await CreateAsync("/product.php?id=8&lang=en", RedirectsApi.ToPage(_offers)).ConfigureAwait(true);

        Assert.Equal("/offers", (await VisitAsync("/product.php?lang=en&id=8").ConfigureAwait(true)).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/product.php?id=8&lang=en&ref=mail").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/product.php?id=8").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_redirect_from_a_query_source_does_not_carry_the_query()
    {
        await CreateAsync("/old.php?id=1", RedirectsApi.ToPath("/new")).ConfigureAwait(true);

        Assert.Equal("/new", (await VisitAsync("/old.php?id=1").ConfigureAwait(true)).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task An_exact_redirect_comes_before_a_prefix_and_the_longest_prefix_wins()
    {
        await CreateAsync("/docs", RedirectsApi.ToPath("/help"), match: "prefix").ConfigureAwait(true);
        await CreateAsync("/docs/api", RedirectsApi.ToPath("/developers"), match: "prefix").ConfigureAwait(true);
        await CreateAsync("/docs/api/old", RedirectsApi.ToPath("/legacy")).ConfigureAwait(true);

        Assert.Equal("/help/intro", (await VisitAsync("/docs/intro").ConfigureAwait(true)).Headers.Location?.OriginalString);
        Assert.Equal("/developers/keys", (await VisitAsync("/docs/api/keys").ConfigureAwait(true)).Headers.Location?.OriginalString);
        Assert.Equal("/legacy", (await VisitAsync("/docs/api/old").ConfigureAwait(true)).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/docs").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_redirect_to_a_page_that_is_not_live_gives_the_not_found_page()
    {
        await CreateAsync("/to-offers", RedirectsApi.ToPage(_offers)).ConfigureAwait(true);
        var unpublished = await PublishingApi.UnpublishAsync(_scenario.Admin, PagesApi.PagePath(_scenario.SiteA, _offers)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);

        var response = await VisitAsync("/to-offers").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_turned_off_redirect_is_ignored()
    {
        var created = await CreateAsync("/off", RedirectsApi.ToPath("/x")).ConfigureAwait(true);
        await RedirectsApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, created.GetProperty("id").GetGuid(), created.GetProperty("version").GetUInt32(), "/off", RedirectsApi.ToPath("/x"), enabled: false).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/off").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_redirect_answers_on_its_own_site_only()
    {
        await CreateAsync("/only-a", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/only-a", PagesScenario.HostB).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_new_redirect_answers_at_once_on_another_instance()
    {
        using var second = new QuickBuilderApp(_scenario.App.ConnectionString);

        await CreateAsync("/fresh", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.MovedPermanently, (await RedirectsApi.VisitAsync(second, PagesScenario.HostA, "/fresh").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_non_ascii_target_is_percent_encoded_in_the_location()
    {
        await CreateAsync("/menu", RedirectsApi.ToPath("/café")).ConfigureAwait(true);

        Assert.Equal("/caf%C3%A9", (await VisitAsync("/menu").ConfigureAwait(true)).Headers.Location?.OriginalString);
    }
}
