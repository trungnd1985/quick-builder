using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// The Pages module's one cross-site read (010 research P-15, 001 FR-013a).
/// </summary>
/// <remarks>
/// Every Pages endpoint is on the administrative surface and reads through
/// <c>PagesDbContext.ForSite</c>. This is its dedicated test: it returns the named site's rows
/// and no other's, the ordinary filters still match nothing on the admin surface, and no
/// other call site in the module crosses sites.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PagesSiteScopingTests(DatabaseFixture database)
{
    [Fact]
    public async Task For_site_returns_only_the_named_sites_rows_of_every_entity()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-scoping").ConfigureAwait(true);
        foreach (var site in new[] { scenario.SiteA, scenario.SiteB })
        {
            var component = (await PagesApi.CreateComponentOkAsync(scenario.Admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
            var page = (await PagesApi.CreateOkAsync(scenario.Admin, site, "about").ConfigureAwait(true)).GetProperty("id").GetGuid();
            await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
                scenario.Admin, PagesApi.PagePath(site, page), PagesApi.DocumentReferencing(component)).ConfigureAwait(true)).ConfigureAwait(true);
        }

        var counts = await scenario.ReadAsync(async db => new[]
        {
            await db.ForSite<PageTree>(scenario.SiteA).CountAsync().ConfigureAwait(false),
            await db.ForSite<Page>(scenario.SiteA).CountAsync().ConfigureAwait(false),
            await db.ForSite<PageDraft>(scenario.SiteA).CountAsync().ConfigureAwait(false),
            await db.ForSite<Component>(scenario.SiteA).CountAsync().ConfigureAwait(false),
            await db.ForSite<ComponentDraft>(scenario.SiteA).CountAsync().ConfigureAwait(false),
            await db.ForSite<ComponentUsage>(scenario.SiteA).CountAsync().ConfigureAwait(false),
        }).ConfigureAwait(true);

        Assert.All(counts, count => Assert.Equal(1, count));

        var siteIds = await scenario.ReadAsync(db => db.ForSite<Page>(scenario.SiteA).Select(p => p.SiteId).Distinct().ToListAsync()).ConfigureAwait(true);
        Assert.Equal([scenario.SiteA], siteIds);
    }

    [Fact]
    public async Task The_query_filters_match_nothing_without_a_resolved_site()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-scoping-filter").ConfigureAwait(true);
        await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "about").ConfigureAwait(true);

        var visible = await scenario.ReadAsync(db => db.Pages.CountAsync()).ConfigureAwait(true);

        Assert.Equal(0, visible);
    }

    [Fact]
    public void For_site_is_the_modules_only_cross_site_read_and_states_a_reason()
    {
        var moduleRoot = Path.Combine(DraftApiTests.FindRepositoryRoot(), "src", "Modules", "Pages");

        var callSites = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .Where(c => c.Line.Contains("AcrossAllSites(", StringComparison.Ordinal))
            .ToList();

        var (file, line) = Assert.Single(callSites);
        Assert.Equal("PagesDbContext.cs", file);
        Assert.Contains("\"Admin operation on a site named explicitly in the route", line, StringComparison.Ordinal);
    }
}
