using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>Shared components over HTTP (010 US4, FR-024 to FR-028).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ComponentApiTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_component_is_created_with_an_empty_draft_and_names_are_unique_ignoring_case()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-create").ConfigureAwait(true);

        var response = await PagesApi.CreateComponentAsync(scenario.Admin, scenario.SiteA, $"{PagesApi.CanaryMarker}Header").ConfigureAwait(true);
        var header = await PagesApi.OkAsync(response, HttpStatusCode.Created).ConfigureAwait(true);

        Assert.EndsWith($"/components/{header.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal(0, header.GetProperty("usageCount").GetInt32());
        Assert.Equal(1, header.GetProperty("draft").GetProperty("schemaVersion").GetInt32());

        await PagesApi.RefusedAsync(
            await PagesApi.CreateComponentAsync(scenario.Admin, scenario.SiteA, $"{PagesApi.CanaryMarker}HEADER").ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.duplicate_component_name").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.CreateComponentAsync(scenario.Admin, scenario.SiteA, "  ").ConfigureAwait(true),
            HttpStatusCode.BadRequest, "request.invalid").ConfigureAwait(true);

        // Another site may use the same name.
        await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteB, $"{PagesApi.CanaryMarker}Header").ConfigureAwait(true);

        var events = await scenario.OutboxAsync().ConfigureAwait(true);
        Assert.Equal(2, events.Count(e => e.Type == "Pages.ComponentCreated"));
        Assert.All(events, e => Assert.DoesNotContain(PagesApi.CanaryMarker, e.Payload, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Usage_follows_page_drafts_and_blocks_deletion()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-usage").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var header = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            home*
            about
            """).ConfigureAwait(true);

        foreach (var path in new[] { "/", "/about" })
        {
            await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
                admin, PagesApi.PagePath(site, ids[path]), PagesApi.DocumentReferencing(header)).ConfigureAwait(true)).ConfigureAwait(true);
        }

        var component = await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, header).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(2, component.GetProperty("usageCount").GetInt32());

        var usages = await PagesApi.OkAsync(await PagesApi.UsagesAsync(admin, site, header).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(["/", "/about"], usages.EnumerateArray().Select(u => u.GetProperty("path").GetString()));

        var version = component.GetProperty("version").GetUInt32();
        var refused = await PagesApi.RefusedAsync(
            await PagesApi.DeleteComponentAsync(admin, site, header, version).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.component_in_use").ConfigureAwait(true);
        Assert.Equal("2", refused.Text("usageCount"));

        foreach (var path in new[] { "/", "/about" })
        {
            await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
                admin, PagesApi.PagePath(site, ids[path]), PagesApi.DocumentReferencing()).ConfigureAwait(true)).ConfigureAwait(true);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PagesApi.DeleteComponentAsync(admin, site, header, version).ConfigureAwait(true)).StatusCode);
        Assert.Contains(await scenario.OutboxAsync().ConfigureAwait(true), e => e.Type == "Pages.ComponentDeleted");
    }

    [Fact]
    public async Task A_reference_to_another_sites_component_looks_like_an_unknown_one()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-other").ConfigureAwait(true);
        var onB = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteB, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());

        var otherSite = await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, PagesApi.DocumentReferencing(onB)).ConfigureAwait(true),
            HttpStatusCode.BadRequest, "pages.unknown_component").ConfigureAwait(true);
        var random = await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, PagesApi.DocumentReferencing(Guid.NewGuid())).ConfigureAwait(true),
            HttpStatusCode.BadRequest, "pages.unknown_component").ConfigureAwait(true);

        Assert.Equal(otherSite.Text("detail"), random.Text("detail"));
    }

    [Fact]
    public async Task A_rename_keeps_references_and_a_stale_rename_is_out_of_date()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-rename").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var footer = await PagesApi.CreateComponentOkAsync(admin, site, "Footer").ConfigureAwait(true);
        var id = footer.GetProperty("id").GetGuid();
        var page = PagesApi.PagePath(site, (await PagesApi.CreateOkAsync(admin, site, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(admin, page, PagesApi.DocumentReferencing(id)).ConfigureAwait(true)).ConfigureAwait(true);

        var renamed = await PagesApi.OkAsync(
            await PagesApi.RenameComponentAsync(admin, site, id, "Site footer", footer.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(("Site footer", 1), (renamed.GetProperty("name").GetString(), renamed.GetProperty("usageCount").GetInt32()));
        await PagesApi.RefusedAsync(
            await PagesApi.RenameComponentAsync(admin, site, id, "Again", footer.GetProperty("version").GetUInt32()).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);
        await PagesApi.OkAsync(
            await PagesApi.RenameComponentAsync(admin, site, id, "SITE FOOTER", renamed.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_component_draft_round_trips_but_may_not_contain_a_component()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-draft").ConfigureAwait(true);
        var header = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var other = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Other").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = PagesApi.ComponentPath(scenario.SiteA, header);

        var problem = await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(scenario.Admin, path, PagesApi.DocumentReferencing(other)).ConfigureAwait(true),
            HttpStatusCode.BadRequest, "pages.invalid_document").ConfigureAwait(true);
        Assert.Equal("component_ref_not_allowed", problem.Text("rule"));

        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, path, PagesApi.DocumentReferencing()).ConfigureAwait(true)).ConfigureAwait(true);
        var draft = await PagesApi.DraftOkAsync(scenario.Admin, path).ConfigureAwait(true);
        Assert.Equal("root", draft.GetProperty("document").GetProperty("root").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Another_sites_component_is_not_found_here()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-notfound").ConfigureAwait(true);
        var onB = await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteB, "Header").ConfigureAwait(true);
        var id = onB.GetProperty("id").GetGuid();

        await PagesApi.RefusedAsync(await PagesApi.GetComponentAsync(scenario.Admin, scenario.SiteA, id).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.component_not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PagesApi.UsagesAsync(scenario.Admin, scenario.SiteA, id).ConfigureAwait(true), HttpStatusCode.NotFound, "pages.component_not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.DeleteComponentAsync(scenario.Admin, scenario.SiteA, id, onB.GetProperty("version").GetUInt32()).ConfigureAwait(true),
            HttpStatusCode.NotFound, "pages.component_not_found").ConfigureAwait(true);
    }
}
