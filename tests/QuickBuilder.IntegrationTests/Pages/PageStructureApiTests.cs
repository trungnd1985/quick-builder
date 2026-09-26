using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>Renaming, moving and choosing the home page over HTTP (010 US2, FR-008 to FR-010).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PageStructureApiTests(DatabaseFixture database)
{
    private const string Outline = """
        home*
        about
          team
        services
          hosting
            plans
            faq
        contact
        """;

    [Fact]
    public async Task A_new_slug_recomputes_every_descendant_in_one_event()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-reslug").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, Outline).ConfigureAwait(true);

        var response = await PagesApi.UpdateAsync(scenario.Admin, scenario.SiteA, ids["/services"], "What we do", "what-we-do", 1).ConfigureAwait(true);
        var page = await PagesApi.OkAsync(response).ConfigureAwait(true);

        Assert.Equal(("/what-we-do", 2), (page.GetProperty("path").GetString(), page.GetProperty("version").GetInt32()));
        Assert.Contains("/what-we-do/hosting/plans", await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true));

        var changed = (await scenario.OutboxAsync().ConfigureAwait(true)).Where(e => e.Type == "Pages.PageAddressesChanged").ToList();
        using var payload = JsonDocument.Parse(Assert.Single(changed).Payload);
        Assert.Equal(4, payload.RootElement.GetProperty("Changes").GetArrayLength());
    }

    [Fact]
    public async Task A_title_change_keeps_the_address()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-retitle").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, Outline).ConfigureAwait(true);

        var page = await PagesApi.OkAsync(
            await PagesApi.UpdateAsync(scenario.Admin, scenario.SiteA, ids["/about"], "About us", "about", 1).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(("About us", "/about"), (page.GetProperty("title").GetString(), page.GetProperty("path").GetString()));
        Assert.DoesNotContain(await scenario.OutboxAsync().ConfigureAwait(true), e => e.Type == "Pages.PageAddressesChanged");
    }

    [Fact]
    public async Task Moving_a_subtree_returns_the_whole_tree()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-move").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, Outline).ConfigureAwait(true);

        var tree = await PagesApi.OkAsync(
            await PagesApi.MoveAsync(scenario.Admin, scenario.SiteA, ids["/services/hosting"], ids["/about"], ids["/about/team"], 1).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(
            ["/", "/about", "/about/team", "/about/hosting", "/about/hosting/plans", "/about/hosting/faq", "/services", "/contact"],
            tree.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task Reordering_puts_contact_first()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-reorder").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, Outline).ConfigureAwait(true);

        await PagesApi.OkAsync(
            await PagesApi.MoveAsync(scenario.Admin, scenario.SiteA, ids["/contact"], null, ids["/"], 1).ConfigureAwait(true)).ConfigureAwait(true);

        var top = (await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true)).Where(p => p.Count(c => c == '/') == 1).ToList();
        Assert.Equal(["/", "/contact", "/about", "/services"], top);
    }

    [Fact]
    public async Task Refused_changes_leave_the_tree_exactly_as_it_was()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-refused").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var ids = await PagesApi.SeedTreeAsync(admin, site, Outline).ConfigureAwait(true);
        await PagesApi.CreateOkAsync(admin, site, "faq", ids["/about"]).ConfigureAwait(true);
        var before = (await PagesApi.TreeOkAsync(admin, site).ConfigureAwait(true)).GetRawText();

        await PagesApi.RefusedAsync(
            await PagesApi.MoveAsync(admin, site, ids["/about"], ids["/about/team"], null, 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.move_into_descendant").ConfigureAwait(true);
        var clash = await PagesApi.RefusedAsync(
            await PagesApi.MoveAsync(admin, site, ids["/services/hosting/faq"], ids["/about"], null, 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.duplicate_address").ConfigureAwait(true);
        Assert.Contains("/about/faq", clash.Text("detail"), StringComparison.Ordinal);
        await PagesApi.RefusedAsync(
            await PagesApi.UpdateAsync(admin, site, ids["/contact"], "x", "health", 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.reserved_address").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.MoveAsync(admin, site, ids["/"], ids["/about"], null, 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.home_page_must_be_top_level").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.MakeHomeAsync(admin, site, ids["/about/team"], 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.home_page_must_be_top_level").ConfigureAwait(true);

        Assert.Equal(before, (await PagesApi.TreeOkAsync(admin, site).ConfigureAwait(true)).GetRawText());
    }

    [Fact]
    public async Task A_stale_version_or_a_departed_anchor_is_out_of_date()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-stale").ConfigureAwait(true);
        var admin = scenario.Admin;
        var ids = await PagesApi.SeedTreeAsync(admin, scenario.SiteA, Outline).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(admin, scenario.SiteA, ids["/contact"], "Contact us", "contact", 1).ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PagesApi.UpdateAsync(admin, scenario.SiteA, ids["/contact"], "Other", "contact", 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.MoveAsync(admin, scenario.SiteA, ids["/about"], null, ids["/about/team"], 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);
    }

    [Fact]
    public async Task Make_home_swaps_the_addresses_and_publishes_both_events()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-make-home").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            home*
              news
            about
              team
            """).ConfigureAwait(true);

        var tree = await PagesApi.OkAsync(
            await PagesApi.MakeHomeAsync(scenario.Admin, scenario.SiteA, ids["/about"], 1).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(["/home", "/home/news", "/", "/team"], tree.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("path").GetString()));
        var types = (await scenario.OutboxAsync().ConfigureAwait(true)).Select(e => e.Type).ToList();
        Assert.Equal(2, types.Count(t => t == "Pages.HomePageChanged"));
        Assert.Single(types, t => t == "Pages.PageAddressesChanged");
    }
}
