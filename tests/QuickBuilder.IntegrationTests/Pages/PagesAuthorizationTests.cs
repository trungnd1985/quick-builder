using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Who may do what with pages and components: Designer builds, Editor writes
/// (010 clarification Q3, FR-029, FR-030, US5 #4 and #5).
/// </summary>
/// <remarks>
/// Every endpoint, for each role. A role on the site that lacks the capability is told
/// 403; a user with no role on the site is told the site does not exist (002).
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PagesAuthorizationTests(DatabaseFixture database)
{
    public enum Caller
    {
        Administrator,
        Designer,
        Editor,
        NoGrant,
    }

    [Theory]
    [InlineData(Caller.Administrator, true, true, true)]
    [InlineData(Caller.Designer, true, true, true)]
    [InlineData(Caller.Editor, false, true, false)]
    public async Task Each_role_gets_exactly_its_capabilities(Caller caller, bool structure, bool pageDrafts, bool components)
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-authz").ConfigureAwait(true);
        var site = scenario.SiteA;
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, site, """
            home*
            about
            contact
            """).ConfigureAwait(true);
        var header = await PagesApi.CreateComponentOkAsync(scenario.Admin, site, "Header").ConfigureAwait(true);
        var headerId = header.GetProperty("id").GetGuid();
        var client = await ClientAsync(scenario, caller).ConfigureAwait(true);

        // Viewing: every role on the site.
        foreach (var view in new Func<Task<HttpResponseMessage>>[]
        {
            () => PagesApi.TreeAsync(client, site),
            () => PagesApi.GetAsync(client, site, ids["/about"]),
            () => PagesApi.DraftAsync(client, PagesApi.PagePath(site, ids["/about"])),
            () => PagesApi.ListComponentsAsync(client, site),
            () => PagesApi.GetComponentAsync(client, site, headerId),
            () => PagesApi.UsagesAsync(client, site, headerId),
            () => PagesApi.DraftAsync(client, PagesApi.ComponentPath(site, headerId)),
        })
        {
            Assert.Equal(HttpStatusCode.OK, (await view().ConfigureAwait(true)).StatusCode);
        }

        var tree = await PagesApi.TreeOkAsync(client, site).ConfigureAwait(true);
        var permissions = tree.GetProperty("permissions");
        Assert.Equal(
            (structure, pageDrafts, components),
            (permissions.GetProperty("canManageStructure").GetBoolean(),
             permissions.GetProperty("canEditPageDrafts").GetBoolean(),
             permissions.GetProperty("canManageComponents").GetBoolean()));

        // Page drafts: Designer and Editor alike.
        await ExpectAsync(pageDrafts, HttpStatusCode.OK,
            PagesApi.SaveCurrentDraftAsync(client, PagesApi.PagePath(site, ids["/about"]), PagesApi.DocumentReferencing())).ConfigureAwait(true);

        // Structure: Designer only.
        await ExpectAsync(structure, HttpStatusCode.Created, PagesApi.CreateAsync(client, site, "new")).ConfigureAwait(true);
        await ExpectAsync(structure, HttpStatusCode.OK, PagesApi.UpdateAsync(client, site, ids["/about"], "About us", "about", 1)).ConfigureAwait(true);
        await ExpectAsync(structure, HttpStatusCode.OK, PagesApi.MoveAsync(client, site, ids["/contact"], null, null, 1)).ConfigureAwait(true);
        await ExpectAsync(structure, HttpStatusCode.OK, PagesApi.MakeHomeAsync(client, site, ids["/contact"], structure ? 2 : 1)).ConfigureAwait(true);
        await ExpectAsync(structure, HttpStatusCode.NoContent, PagesApi.DeleteAsync(client, site, ids["/"], 2)).ConfigureAwait(true);

        // Components: Designer only.
        await ExpectAsync(components, HttpStatusCode.Created, PagesApi.CreateComponentAsync(client, site, "Footer")).ConfigureAwait(true);
        await ExpectAsync(components, HttpStatusCode.OK,
            PagesApi.SaveCurrentDraftAsync(client, PagesApi.ComponentPath(site, headerId), PagesApi.DocumentReferencing())).ConfigureAwait(true);
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(client, site, headerId).ConfigureAwait(true)).ConfigureAwait(true))
            .GetProperty("version").GetUInt32();
        var renamed = await PagesApi.RenameComponentAsync(client, site, headerId, "Site header", version).ConfigureAwait(true);
        await ExpectAsync(components, HttpStatusCode.OK, Task.FromResult(renamed)).ConfigureAwait(true);
        if (components)
        {
            version = (await PagesApi.OkAsync(renamed).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        }

        await ExpectAsync(components, HttpStatusCode.NoContent, PagesApi.DeleteComponentAsync(client, site, headerId, version)).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_user_without_a_role_on_the_site_is_told_it_does_not_exist()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-authz-none").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "about").ConfigureAwait(true);
        var header = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var client = await ClientAsync(scenario, Caller.NoGrant).ConfigureAwait(true);
        var site = scenario.SiteA;

        foreach (var request in new Func<Task<HttpResponseMessage>>[]
        {
            () => PagesApi.TreeAsync(client, site),
            () => PagesApi.GetAsync(client, site, ids["/about"]),
            () => PagesApi.CreateAsync(client, site, "x"),
            () => PagesApi.UpdateAsync(client, site, ids["/about"], "x", "x", 1),
            () => PagesApi.MoveAsync(client, site, ids["/about"], null, null, 1),
            () => PagesApi.MakeHomeAsync(client, site, ids["/about"], 1),
            () => PagesApi.DeleteAsync(client, site, ids["/about"], 1),
            () => PagesApi.DraftAsync(client, PagesApi.PagePath(site, ids["/about"])),
            () => PagesApi.SaveDraftAsync(client, PagesApi.PagePath(site, ids["/about"]), PagesApi.DocumentReferencing(), 1),
            () => PagesApi.ListComponentsAsync(client, site),
            () => PagesApi.CreateComponentAsync(client, site, "x"),
            () => PagesApi.GetComponentAsync(client, site, header),
            () => PagesApi.RenameComponentAsync(client, site, header, "x", 1),
            () => PagesApi.DeleteComponentAsync(client, site, header, 1),
            () => PagesApi.UsagesAsync(client, site, header),
            () => PagesApi.DraftAsync(client, PagesApi.ComponentPath(site, header)),
            () => PagesApi.SaveDraftAsync(client, PagesApi.ComponentPath(site, header), PagesApi.DocumentReferencing(), 1),
        })
        {
            await PagesApi.RefusedAsync(await request().ConfigureAwait(true), HttpStatusCode.NotFound, "sites.not_found").ConfigureAwait(true);
        }
    }

    private static async Task ExpectAsync(bool allowed, HttpStatusCode success, Task<HttpResponseMessage> request)
    {
        var response = await request.ConfigureAwait(false);
        if (allowed)
        {
            Assert.True(response.StatusCode == success, $"Expected {(int)success}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        }
        else
        {
            await PagesApi.RefusedAsync(response, HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(false);
        }
    }

    private static Task<HttpClient> ClientAsync(PagesScenario scenario, Caller caller) => caller switch
    {
        Caller.Administrator => Task.FromResult(scenario.Admin),
        Caller.Designer => scenario.UserAsync((scenario.SiteA, SystemRole.Designer)),
        Caller.Editor => scenario.UserAsync((scenario.SiteA, SystemRole.Editor)),
        _ => scenario.UserAsync((scenario.SiteB, SystemRole.Designer)),
    };
}
