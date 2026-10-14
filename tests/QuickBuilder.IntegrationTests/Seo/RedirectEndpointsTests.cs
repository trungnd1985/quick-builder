using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 2: Designers manage a site's redirects (017 FR-015, FR-017, FR-018, FR-020;
/// research SE-09, SE-13; seo-api.yaml).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectEndpointsTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _about;
    private Guid _draftOnly;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-redirects").ConfigureAwait(false);
        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nabout\ndraft-only").ConfigureAwait(false);
        _about = pages["/about"];
        _draftOnly = pages["/draft-only"];
        var aboutPath = PagesApi.PagePath(_scenario.SiteA, _about);
        await PublishingApi.SaveAsync(_scenario.Admin, aboutPath, PublishingApi.HeadingDocument("About")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, aboutPath).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Admin => _scenario.Admin;

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task A_redirect_is_created_normalised_and_read_back_with_its_history()
    {
        var created = await RedirectsApi.CreateOkAsync(Admin, Site, "/About-Us.html/", RedirectsApi.ToPath("/new"), note: "From the old site").ConfigureAwait(true);

        Assert.Equal("/about-us.html", created.GetProperty("source").GetString());
        Assert.Equal("exact", created.GetProperty("match").GetString());
        Assert.Equal("/new", created.GetProperty("targetAddress").GetString());
        Assert.Equal("active", created.GetProperty("state").GetString());
        Assert.Equal("manual", created.GetProperty("origin").GetString());
        Assert.False(created.GetProperty("shortened").GetBoolean());

        var id = created.GetProperty("id").GetGuid();
        var read = await PagesApi.OkAsync(await RedirectsApi.GetAsync(Admin, Site, id).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("From the old site", read.GetProperty("note").GetString());
        Assert.Equal("created", read.GetProperty("history")[0].GetProperty("action").GetString());
    }

    [Fact]
    public async Task A_path_that_is_a_page_s_address_is_stored_as_that_page()
    {
        var created = await RedirectsApi.CreateOkAsync(Admin, Site, "/old-about", RedirectsApi.ToPath("/about")).ConfigureAwait(true);

        Assert.Equal("page", created.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(_about, created.GetProperty("target").GetProperty("pageId").GetGuid());
    }

    [Fact]
    public async Task An_absolute_address_on_the_primary_host_name_is_stored_as_a_path()
    {
        var created = await RedirectsApi.CreateOkAsync(Admin, Site, "/old-x", RedirectsApi.ToUrl($"https://{PagesScenario.HostA}/new-x?a=1")).ConfigureAwait(true);

        Assert.Equal("path", created.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal("/new-x?a=1", created.GetProperty("target").GetProperty("path").GetString());
    }

    [Fact]
    public async Task A_second_redirect_from_the_same_source_is_refused_naming_the_first()
    {
        var first = await RedirectsApi.CreateOkAsync(Admin, Site, "/dup", RedirectsApi.ToPath("/a")).ConfigureAwait(true);

        var response = await RedirectsApi.CreateAsync(Admin, Site, "/DUP/", RedirectsApi.ToPath("/b")).ConfigureAwait(true);

        var problem = await PagesApi.RefusedAsync(response, HttpStatusCode.Conflict, "redirects.duplicate").ConfigureAwait(true);
        Assert.Equal(first.GetProperty("id").GetString(), problem.Text("redirectId"));
    }

    [Fact]
    public async Task A_loop_is_refused_naming_the_redirect_that_closes_it()
    {
        var first = await RedirectsApi.CreateOkAsync(Admin, Site, "/loop-a", RedirectsApi.ToPath("/loop-b")).ConfigureAwait(true);

        var response = await RedirectsApi.CreateAsync(Admin, Site, "/loop-b", RedirectsApi.ToPath("/loop-a")).ConfigureAwait(true);

        var problem = await PagesApi.RefusedAsync(response, HttpStatusCode.Conflict, "redirects.loop").ConfigureAwait(true);
        Assert.Equal(first.GetProperty("id").GetString(), problem.Text("redirectId"));
    }

    [Fact]
    public async Task A_chain_is_stored_as_one_hop_and_says_so()
    {
        await RedirectsApi.CreateOkAsync(Admin, Site, "/chain-b", RedirectsApi.ToPath("/chain-c")).ConfigureAwait(true);

        var created = await RedirectsApi.CreateOkAsync(Admin, Site, "/chain-a", RedirectsApi.ToPath("/chain-b")).ConfigureAwait(true);

        Assert.True(created.GetProperty("shortened").GetBoolean());
        Assert.Equal("/chain-c", created.GetProperty("targetAddress").GetString());
    }

    [Theory]
    [InlineData("/api/x")]
    [InlineData("/sitemap.xml")]
    [InlineData("/")]
    [InlineData("no-slash")]
    public async Task A_source_the_platform_answers_or_that_is_no_path_is_refused(string source)
    {
        var response = await RedirectsApi.CreateAsync(Admin, Site, source, RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "redirects.source_invalid").ConfigureAwait(true);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pw@elsewhere.test/")]
    [InlineData("//elsewhere.test/")]
    public async Task A_target_with_another_scheme_or_user_info_is_refused(string url)
    {
        var response = await RedirectsApi.CreateAsync(Admin, Site, "/old", RedirectsApi.ToUrl(url)).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "redirects.target_invalid").ConfigureAwait(true);
    }

    [Fact]
    public async Task Another_site_s_page_is_an_unknown_target()
    {
        var other = await PagesApi.SeedTreeAsync(Admin, _scenario.SiteB, "elsewhere").ConfigureAwait(true);

        var response = await RedirectsApi.CreateAsync(Admin, Site, "/old", RedirectsApi.ToPage(other["/elsewhere"])).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "redirects.target_invalid").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_redirect_is_changed_with_its_version_and_a_stale_one_is_refused()
    {
        var created = await RedirectsApi.CreateOkAsync(Admin, Site, "/v", RedirectsApi.ToPath("/a")).ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();

        var changed = await PagesApi.OkAsync(await RedirectsApi.UpdateAsync(Admin, Site, id, version, "/v", RedirectsApi.ToPath("/b"), 302).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(302, changed.GetProperty("statusCode").GetInt32());

        var stale = await RedirectsApi.UpdateAsync(Admin, Site, id, version, "/v", RedirectsApi.ToPath("/c")).ConfigureAwait(true);
        await PagesApi.RefusedAsync(stale, HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);
    }

    [Fact]
    public async Task Turning_a_redirect_off_and_deleting_it_are_recorded()
    {
        var created = await RedirectsApi.CreateOkAsync(Admin, Site, "/gone", RedirectsApi.ToPath("/a")).ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();

        var off = await PagesApi.OkAsync(await RedirectsApi.UpdateAsync(
            Admin, Site, id, created.GetProperty("version").GetUInt32(), "/gone", RedirectsApi.ToPath("/a"), enabled: false).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("off", off.GetProperty("state").GetString());

        var deleted = await RedirectsApi.DeleteAsync(Admin, Site, id, off.GetProperty("version").GetUInt32()).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RedirectsApi.GetAsync(Admin, Site, id).ConfigureAwait(true)).StatusCode);

        var actions = await _scenario.ReadAsync(db => PagesScenario.Everything<RedirectChange>(db)
            .Where(c => c.RedirectId == id)
            .OrderBy(c => c.AtUtc)
            .Select(c => c.Action)
            .ToListAsync()).ConfigureAwait(true);
        Assert.Equal(["created", "turned_off", "deleted"], actions);
    }

    [Fact]
    public async Task The_list_marks_hidden_and_broken_redirects_and_searches_and_filters()
    {
        await RedirectsApi.CreateOkAsync(Admin, Site, "/about", RedirectsApi.ToPath("/elsewhere")).ConfigureAwait(true);
        await RedirectsApi.CreateOkAsync(Admin, Site, "/to-draft", RedirectsApi.ToPage(_draftOnly)).ConfigureAwait(true);
        await RedirectsApi.CreateOkAsync(Admin, Site, "/fine", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        var list = await PagesApi.OkAsync(await RedirectsApi.ListAsync(Admin, Site).ConfigureAwait(true)).ConfigureAwait(true);
        var states = list.GetProperty("items").EnumerateArray().ToDictionary(r => r.GetProperty("source").GetString()!, r => r.GetProperty("state").GetString());
        Assert.Equal("hidden", states["/about"]);
        Assert.Equal("broken", states["/to-draft"]);
        Assert.Equal("active", states["/fine"]);
        Assert.Equal(10000, list.GetProperty("limit").GetInt32());

        var hidden = await PagesApi.OkAsync(await RedirectsApi.ListAsync(Admin, Site, "?state=hidden").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(["/about"], hidden.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("source").GetString()));

        var searched = await PagesApi.OkAsync(await RedirectsApi.ListAsync(Admin, Site, "?q=FIN").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(1, searched.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_site_holds_at_most_ten_thousand_redirects()
    {
        await _scenario.ReadAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO pages.redirects (id, site_id, match_kind, source_path, source_query, status_code, target_kind, target_path, enabled, origin, created_at_utc, updated_at_utc)
                SELECT gen_random_uuid(), {0}, 'exact', '/bulk-' || n, '', 301, 'path', '/x', true, 'import', {1}, {1}
                FROM generate_series(1, 10000) AS n
                """,
                Site,
                now).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(true);

        var response = await RedirectsApi.CreateAsync(Admin, Site, "/one-more", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.Conflict, "redirects.limit_reached").ConfigureAwait(true);
    }

    [Fact]
    public async Task Designers_manage_redirects_editors_may_not_and_other_sites_see_nothing()
    {
        var designer = await _scenario.UserAsync((Site, SystemRole.Designer)).ConfigureAwait(true);
        var editor = await _scenario.UserAsync((Site, SystemRole.Editor)).ConfigureAwait(true);
        var outsider = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, (await RedirectsApi.CreateAsync(designer, Site, "/by-designer", RedirectsApi.ToPath("/x")).ConfigureAwait(true)).StatusCode);
        await PagesApi.RefusedAsync(await RedirectsApi.ListAsync(editor, Site).ConfigureAwait(true), HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await RedirectsApi.ListAsync(outsider, Site).ConfigureAwait(true), HttpStatusCode.NotFound, "sites.not_found").ConfigureAwait(true);
    }

}
