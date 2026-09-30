using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Sites;
using QuickBuilder.Rendering.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// User Story 6, SC-006, FR-032: a change re-renders exactly the pages it affects (012 research
/// PR-04, PR-05; data-model §3.2). Renders are counted, so a page served from the cache and a
/// page rendered again are told apart.
/// </summary>
/// <remarks>
/// 50 live pages: <c>/p00</c> to <c>/p09</c> show component C; <c>/p40</c> to <c>/p42</c>
/// link to <c>/p49</c>; the rest stand alone.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class CacheInvalidationTests(DatabaseFixture database) : IAsyncLifetime
{
    private const int PageCount = 50;

    private PagesScenario _scenario = null!;
    private readonly Dictionary<string, Guid> _pages = [];
    private Guid _component;
    private string _componentPath = string.Empty;

    private RenderMetrics Metrics => _scenario.App.Services.GetRequiredService<RenderMetrics>();

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "cache-invalidation").ConfigureAwait(false);

        _component = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "C").ConfigureAwait(false)).GetProperty("id").GetGuid();
        _componentPath = PagesApi.ComponentPath(_scenario.SiteA, _component);
        await PublishingApi.SaveAsync(_scenario.Admin, _componentPath, PublishingApi.ComponentDocument("Component one")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _componentPath).ConfigureAwait(false);

        for (var i = 0; i < PageCount; i++)
        {
            var slug = $"p{i:00}";
            _pages[$"/{slug}"] = (await PagesApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, slug).ConfigureAwait(false)).GetProperty("id").GetGuid();
        }

        // Pages linking to /p49 are saved after it exists.
        for (var i = 0; i < PageCount; i++)
        {
            var address = $"/p{i:00}";
            var document = i < 10
                ? PublishingApi.HeadingDocument(address, _component)
                : i is >= 40 and <= 42
                    ? LinkingDocument(address, _pages["/p49"])
                    : PublishingApi.HeadingDocument(address);

            var path = Path(address);
            await PublishingApi.SaveAsync(_scenario.Admin, path, document).ConfigureAwait(false);
            await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
        }
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_second_visit_is_served_from_the_cache()
    {
        Assert.Equal(PageCount, await VisitAllAsync().ConfigureAwait(true));
        Assert.Equal(0, await VisitAllAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Publishing_a_component_re_renders_exactly_its_users()
    {
        await VisitAllAsync().ConfigureAwait(true);

        await PublishingApi.SaveAsync(_scenario.Admin, _componentPath, PublishingApi.ComponentDocument("Component two")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _componentPath).ConfigureAwait(true);

        Assert.Equal(10, await VisitAllAsync().ConfigureAwait(true));
        Assert.Contains("Component two", await VisitAsync("/p03").ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_change_re_renders_it_and_the_pages_linking_to_it()
    {
        await VisitAllAsync().ConfigureAwait(true);

        // Publish: the page and its three linkers.
        await PublishingApi.SaveAsync(_scenario.Admin, Path("/p49"), PublishingApi.HeadingDocument("Changed")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, Path("/p49")).ConfigureAwait(true);
        Assert.Equal(4, await VisitAllAsync().ConfigureAwait(true));

        // Rollback: likewise.
        await PagesApi.OkAsync(await PublishingApi.RollbackAsync(_scenario.Admin, Path("/p49"), 1).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(4, await VisitAllAsync().ConfigureAwait(true));

        // Unpublish: the linkers only — the page itself is now not found, which is not rendered.
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, Path("/p49")).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(3, await VisitAllAsync().ConfigureAwait(true));
        Assert.DoesNotContain("href=\"/p49\"", await VisitAsync("/p40").ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_address_re_renders_the_pages_linking_to_it()
    {
        await VisitAllAsync().ConfigureAwait(true);

        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/p49").ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, _pages["/p49"], node.GetProperty("title").GetString()!, "moved", node.GetProperty("version").GetInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        _pages["/moved"] = _pages["/p49"];
        _pages.Remove("/p49");

        // The moved page itself keeps its key: its own content did not change.
        Assert.Equal(3, await VisitAllAsync().ConfigureAwait(true));
        Assert.Contains("href=\"/moved\"", await VisitAsync("/p41").ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_design_publish_or_a_site_change_re_renders_every_page()
    {
        await VisitAllAsync().ConfigureAwait(true);

        var tokens = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(
            new Uri($"/api/v1/editor/sites/{_scenario.SiteA}/design-tokens", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await _scenario.Admin.PostAsync(
            new Uri($"/api/v1/editor/sites/{_scenario.SiteA}/design-tokens/publish", UriKind.Relative),
            JsonContent(new { version = tokens.GetProperty("version").GetUInt32() })).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(PageCount, await VisitAllAsync().ConfigureAwait(true));

        var site = await SitesApi.GetOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        await PagesApi.OkAsync(await SitesApi.PutSettingsAsync(
            _scenario.Admin, _scenario.SiteA, "Site A, renamed", site.GetProperty("scheme").GetString()!, "fr",
            site.GetProperty("primaryHostName").GetString()!, site.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(PageCount, await VisitAllAsync().ConfigureAwait(true));
        Assert.Contains("lang=\"fr\"", await VisitAsync("/p20").ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publishing_a_component_that_two_thousand_pages_show_stays_within_budget()
    {
        // Plan Risks: the bump is one statement over the reverse index. The pages are seeded
        // straight into the publishing tables; only the bump is being measured.
        var siteId = _scenario.SiteA;
        await _scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            """
            WITH seeded AS (SELECT gen_random_uuid() AS id FROM generate_series(1, 2000))
            , pub AS (
                INSERT INTO pages.publications (subject_kind, subject_id, site_id, next_number, generation)
                SELECT 'page', id, {0}, 1, 100 FROM seeded RETURNING subject_id)
            INSERT INTO pages.live_dependencies (site_id, subject_kind, subject_id, target_kind, target_id)
            SELECT {0}, 'page', subject_id, 'component', {1} FROM pub
            """,
            siteId,
            _component)).ConfigureAwait(true);

        // Once to warm up, once to measure.
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _componentPath).ConfigureAwait(true);
        var watch = Stopwatch.StartNew();
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _componentPath).ConfigureAwait(true);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 500, $"Publishing took {watch.ElapsedMilliseconds} ms.");
        // Seeded at generation 100, so two publishes leave exactly them at 102.
        Assert.Equal(2000, await _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM pages.publications WHERE site_id = {0} AND subject_kind = 'page' AND generation = 102", siteId)
            .SingleAsync()).ConfigureAwait(true));
    }

    private string Path(string address) => PagesApi.PagePath(_scenario.SiteA, _pages[address]);

    /// <summary>Visits every live page and returns how many had to be rendered.</summary>
    private async Task<int> VisitAllAsync()
    {
        var before = Metrics.Renders;
        foreach (var address in _pages.Keys)
        {
            await _scenario.App.CreateClientForHost(PagesScenario.HostA).GetAsync(new Uri(address, UriKind.Relative)).ConfigureAwait(false);
        }

        return (int)(Metrics.Renders - before);
    }

    private Task<string> VisitAsync(string address) =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri(address, UriKind.Relative));

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    /// <summary>A page with a heading and a Button linking to <paramref name="target"/>.</summary>
    private static string LinkingDocument(string heading, Guid target) =>
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":[{"id":"h1","type":"heading","props":{"text":"""
        + JsonSerializer.Serialize(heading)
        + ""","level":1},"children":[]},{"id":"b1","type":"button","props":{"label":"Go","link":{"type":"page","pageId":"""
        + JsonSerializer.Serialize(target.ToString())
        + """}},"children":[]}]}]}}""";
}
