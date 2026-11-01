using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Domain;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Concurrency;

/// <summary>
/// Draft saves are fenced by editing sessions (023 research CE-07, FR-018, FR-026): a session
/// that does not hold a live lock cannot save while another does, and without a live lock the
/// version check alone decides.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DraftFencingTests(DatabaseFixture database)
{
    private const string Header = "X-QB-Editing-Session";

    [Fact]
    public async Task A_page_or_component_save_from_a_session_without_the_lock_is_refused_while_another_holds_it()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "fencing-pages");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        var component = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid();

        foreach (var (kind, id, path) in new[]
        {
            ("page", page, PagesApi.PagePath(scenario.SiteA, page)),
            ("component", component, PagesApi.ComponentPath(scenario.SiteA, component)),
        })
        {
            var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, kind, id));
            var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, kind, id));
            await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);
            var before = (await PagesApi.DraftOkAsync(anna, path)).GetProperty("document").GetRawText();

            var refused = await PagesApi.RefusedAsync(
                await PagesApi.SaveDraftWithHeaderAsync(ben, path, PagesApi.DocumentWithText("zz-canary-ben"), Header, bens.ToString()),
                HttpStatusCode.Conflict,
                "editing.lock_held");
            Assert.Equal("Anna Tran", refused.Body.GetProperty("holder").GetProperty("displayName").GetString());
            Assert.Equal(before, (await PagesApi.DraftOkAsync(anna, path)).GetProperty("document").GetRawText());

            // The holder saves; a save without the header is checked by version only.
            await PagesApi.OkAsync(await PagesApi.SaveDraftWithHeaderAsync(anna, path, PagesApi.DocumentWithText("zz-canary-anna"), Header, annas.ToString()));
            await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(ben, path, PagesApi.DocumentWithText("zz-canary-api")));
        }
    }

    [Fact]
    public async Task An_item_save_from_a_session_without_the_lock_is_refused_while_another_holds_it()
    {
        using var items = await ItemsScenario.CreateAsync(database, "fencing-items");
        var app = items.Cms.App;
        var anna = await IdentitySeed.CreateSignedInClientAsync(app, (await IdentitySeed.SeedUserAsync(app.Services, siteRoles: [(items.Site, SystemRole.Editor)], displayName: "Anna Tran")).Id);
        var ben = await IdentitySeed.CreateSignedInClientAsync(app, (await IdentitySeed.SeedUserAsync(app.Services, siteRoles: [(items.Site, SystemRole.Editor)], displayName: "Ben Hale")).Id);
        var item = await items.PostAsync("post");
        var id = ItemsApi.Id(item);

        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, items.Site, "item", id, items.Posts));
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, items.Site, "item", id, items.Posts));
        await EditingApi.ClaimOkAsync(anna, items.Site, annas);

        var body = ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-ben" }, items.PostsSchemaRevision, ItemsApi.DraftRevision(item));
        await PagesApi.RefusedAsync(await SaveWithSessionAsync(ben, items, id, body, bens), HttpStatusCode.Conflict, "editing.lock_held");
        Assert.NotEqual("zz-canary-ben", ItemsApi.Values(await ItemsApi.GetOkAsync(anna, items.Site, items.Posts, id))["f1"]!.GetValue<string>());

        var annasBody = ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-anna" }, items.PostsSchemaRevision, ItemsApi.DraftRevision(item));
        await PagesApi.OkAsync(await SaveWithSessionAsync(anna, items, id, annasBody, annas));
    }

    [Fact]
    public async Task Without_a_live_lock_saves_are_checked_by_version_alone()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "fencing-no-lock");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (ben, _) = await scenario.MemberAsync("Ben Hale", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        var path = PagesApi.PagePath(scenario.SiteA, page);
        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page));
        var bens = EditingApi.SessionId(await EditingApi.OpenOkAsync(ben, scenario.SiteA, "page", page));
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);

        // Anna's browser vanishes: her lock lapses, and Ben's save goes through on version.
        for (var i = 0; i < 3; i++)
        {
            scenario.Clock.Advance(TimeSpan.FromMinutes(1));
            await EditingApi.HeartbeatOkAsync(ben, scenario.SiteA, bens);
        }

        await PagesApi.OkAsync(await PagesApi.SaveDraftWithHeaderAsync(ben, path, PagesApi.DocumentWithText("zz-canary-ben"), Header, bens.ToString()));

        // Every session gone (a restart, a cleared table): the version check still refuses an old base (FR-026).
        await scenario.ReadAsync(d => EditingScenario.Everything<EditingSession>(d).ExecuteDeleteAsync());
        var version = (await PagesApi.DraftOkAsync(ben, path)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.SaveDraftWithHeaderAsync(anna, path, PagesApi.DocumentWithText("zz-canary-anna"), Header, annas.ToString()));
        await PagesApi.RefusedAsync(await PagesApi.SaveDraftAsync(ben, path, PagesApi.DocumentWithText("zz-canary-late"), version), HttpStatusCode.Conflict, "pages.out_of_date");
    }

    private static async Task<HttpResponseMessage> SaveWithSessionAsync(HttpClient client, ItemsScenario items, Guid itemId, object body, Guid session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(ItemsApi.ItemPath(items.Site, items.Posts, itemId), UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(Header, session.ToString());
        return await client.SendAsync(request);
    }
}
