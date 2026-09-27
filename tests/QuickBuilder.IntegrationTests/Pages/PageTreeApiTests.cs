using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>Building a site's page structure over HTTP (010 US1, FR-001 to FR-007, FR-011, FR-015).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PageTreeApiTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_structure_is_built_and_listed_depth_first_with_computed_addresses()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-tree").ConfigureAwait(true);
        var admin = scenario.Admin;

        await PagesApi.SeedTreeAsync(admin, scenario.SiteA, """
            home*
            about
              team
              faq
            services
              web-design
              hosting
                faq
            contact
            """).ConfigureAwait(true);

        var tree = await PagesApi.TreeOkAsync(admin, scenario.SiteA).ConfigureAwait(true);
        var pages = tree.GetProperty("pages").EnumerateArray().ToList();

        Assert.Equal(
            ["/", "/about", "/about/team", "/about/faq", "/services", "/services/web-design", "/services/hosting", "/services/hosting/faq", "/contact"],
            pages.Select(p => p.GetProperty("path").GetString()));
        Assert.Equal([1, 1, 2, 2, 1, 2, 2, 3, 1], pages.Select(p => p.GetProperty("depth").GetInt32()));
        Assert.Equal(2, pages[4].GetProperty("childCount").GetInt32());
        Assert.True(pages[0].GetProperty("isHome").GetBoolean());
        Assert.True(tree.GetProperty("permissions").GetProperty("canManageStructure").GetBoolean());
    }

    [Fact]
    public async Task A_new_page_has_an_empty_version_1_draft_and_a_location()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-create").ConfigureAwait(true);

        var response = await PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, "about").ConfigureAwait(true);
        var page = await PagesApi.OkAsync(response, HttpStatusCode.Created).ConfigureAwait(true);
        var pageId = page.GetProperty("id").GetGuid();

        Assert.Equal($"/api/v1/pages/sites/{scenario.SiteA}/pages/{pageId}", response.Headers.Location?.OriginalString);
        Assert.Equal("/about", page.GetProperty("path").GetString());
        Assert.Equal(1, page.GetProperty("version").GetInt32());
        Assert.Equal(1, page.GetProperty("draft").GetProperty("schemaVersion").GetInt32());
        Assert.NotEqual(Guid.Empty, page.GetProperty("draft").GetProperty("updatedBy").GetProperty("id").GetGuid());

        var draft = await PagesApi.DraftOkAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, pageId)).ConfigureAwait(true);
        var root = draft.GetProperty("document").GetProperty("root");
        Assert.Equal("root", root.GetProperty("type").GetString());
        Assert.Equal(0, root.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public async Task Placement_follows_after_page_id_absent_null_or_given()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-placement").ConfigureAwait(true);
        var admin = scenario.Admin;
        var a = (await PagesApi.CreateOkAsync(admin, scenario.SiteA, "a").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.CreateOkAsync(admin, scenario.SiteA, "b").ConfigureAwait(true);

        await PagesApi.OkAsync(
            await PagesApi.CreateAsync(admin, scenario.SiteA, new { title = "t", slug = "first", afterPageId = (Guid?)null }).ConfigureAwait(true),
            HttpStatusCode.Created).ConfigureAwait(true);
        await PagesApi.OkAsync(
            await PagesApi.CreateAsync(admin, scenario.SiteA, new { title = "t", slug = "after-a", afterPageId = a }).ConfigureAwait(true),
            HttpStatusCode.Created).ConfigureAwait(true);

        Assert.Equal(["/first", "/a", "/after-a", "/b"], await PagesApi.PathsAsync(admin, scenario.SiteA).ConfigureAwait(true));
    }

    [Fact]
    public async Task The_same_slug_is_accepted_under_different_parents()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-same-slug").ConfigureAwait(true);

        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            services
              faq
            support
              faq
            """).ConfigureAwait(true);

        Assert.Contains("/services/faq", ids.Keys);
        Assert.Contains("/support/faq", ids.Keys);
    }

    [Theory]
    [InlineData("about", null, HttpStatusCode.Conflict, "pages.duplicate_address")]
    [InlineData("admin", null, HttpStatusCode.Conflict, "pages.reserved_address")]
    [InlineData("api", null, HttpStatusCode.Conflict, "pages.reserved_address")]
    [InlineData("about", "home", HttpStatusCode.Conflict, "pages.duplicate_address")]
    public async Task Clashing_and_reserved_addresses_are_refused(string slug, string? under, HttpStatusCode status, string code)
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-refuse").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            home*
            about
            """).ConfigureAwait(true);

        var parent = under is null ? (Guid?)null : ids["/"];
        var response = await PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, slug, parent).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, status, code).ConfigureAwait(true);
        Assert.Equal(["/", "/about"], await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true));
    }

    [Fact]
    public async Task Invalid_titles_and_slugs_are_all_reported_at_once()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-invalid").ConfigureAwait(true);

        var response = await PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, new { title = " ", slug = "Not A Slug" }).ConfigureAwait(true);

        var problem = await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "request.invalid").ConfigureAwait(true);
        var errors = problem.Body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("slug", out _));
        Assert.Empty(await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true));
    }

    [Fact]
    public async Task Home_page_rules_and_unknown_parents_are_refused()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-home-rules").ConfigureAwait(true);
        var admin = scenario.Admin;
        var home = (await PagesApi.CreateOkAsync(admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true)).GetProperty("id").GetGuid();

        await PagesApi.RefusedAsync(
            await PagesApi.CreateAsync(admin, scenario.SiteA, "second", isHome: true).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.home_page_exists").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.CreateAsync(admin, scenario.SiteA, "nested", home, isHome: true).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.home_page_must_be_top_level").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.CreateAsync(admin, scenario.SiteA, "orphan", Guid.NewGuid()).ConfigureAwait(true),
            HttpStatusCode.NotFound, "pages.parent_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_page_of_another_site_is_not_found_here()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-other-site").ConfigureAwait(true);
        var onB = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteB, "about").ConfigureAwait(true)).GetProperty("id").GetGuid();

        await PagesApi.RefusedAsync(
            await PagesApi.GetAsync(scenario.Admin, scenario.SiteA, onB).ConfigureAwait(true),
            HttpStatusCode.NotFound, "pages.not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, "child", onB).ConfigureAwait(true),
            HttpStatusCode.NotFound, "pages.parent_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Creating_publishes_page_created_without_the_title()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pages-created-event").ConfigureAwait(true);

        await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true);

        var events = await scenario.OutboxAsync().ConfigureAwait(true);
        Assert.Equal(["Pages.HomePageChanged", "Pages.PageCreated"], events.Select(e => e.Type).Order(StringComparer.Ordinal));
        Assert.All(events, e => Assert.DoesNotContain(PagesApi.CanaryMarker, e.Payload, StringComparison.Ordinal));

        using var payload = JsonDocument.Parse(events.Single(e => e.Type == "Pages.PageCreated").Payload);
        Assert.Equal("/", payload.RootElement.GetProperty("Path").GetString());
    }
}
