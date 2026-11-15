using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>Deleting pages over HTTP (010 US5, FR-012 to FR-014, clarification Q2); since 021, to the trash.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeletePageTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_leaf_goes_to_the_trash_with_its_draft_frees_its_usages_and_its_address()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-delete").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            about
            services
            contact
            """).ConfigureAwait(true);
        var header = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
            admin, PagesApi.PagePath(site, ids["/services"]), PagesApi.DocumentReferencing(header)).ConfigureAwait(true)).ConfigureAwait(true);

        var deleted = await PagesApi.DeleteAsync(admin, site, ids["/services"], 1).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(["/about", "/contact"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
        Assert.Equal(1, (await PagesApi.NodeAsync(admin, site, "/contact").ConfigureAwait(true)).GetProperty("position").GetInt32());
        // 021 FR-003: the draft stays with the trashed page; its component use goes (TR-06).
        Assert.Equal(1, await scenario.ReadAsync(db => PagesScenario.Everything<PageDraft>(db).CountAsync(d => d.PageId == ids["/services"])).ConfigureAwait(true));
        Assert.Equal(0, await scenario.ReadAsync(db => PagesScenario.Everything<ComponentUsage>(db).CountAsync()).ConfigureAwait(true));
        Assert.Contains(await scenario.OutboxAsync().ConfigureAwait(true), e => e.Type == "Pages.PageDeleted");

        // The component it used is free to go, and the address to reuse.
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, header).ConfigureAwait(true)).ConfigureAwait(true))
            .GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.DeleteComponentAsync(admin, site, header, version).ConfigureAwait(true)).StatusCode);
        await PagesApi.CreateOkAsync(admin, site, "services").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_page_with_children_is_refused_with_their_count()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-delete-parent").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            hosting
              plans
              faq
            """).ConfigureAwait(true);

        var problem = await PagesApi.RefusedAsync(
            await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/hosting"], 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.has_children").ConfigureAwait(true);

        Assert.Equal("2", problem.Text("childCount"));
        Assert.Equal(3, (await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true)).Length);
    }

    [Fact]
    public async Task The_home_page_is_refused_while_other_pages_exist()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-delete-home").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            home*
            about
            """).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/"], 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.home_page").ConfigureAwait(true);

        await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/about"], 1).ConfigureAwait(true);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/"], 1).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_stale_version_is_out_of_date()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-delete-stale").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "about").ConfigureAwait(true);
        await PagesApi.UpdateAsync(scenario.Admin, scenario.SiteA, ids["/about"], "About us", "about", 1).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, ids["/about"], 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);
    }
}
