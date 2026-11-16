using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// "Save my version as a copy" for pages and shared components (023 US4, FR-020 to FR-022,
/// research CE-11).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CopyEndpointsTests(DatabaseFixture database)
{
    private static readonly DateTimeOffset LoadedAt = new(2026, 10, 25, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_page_copy_goes_right_after_the_original_with_the_version_as_its_draft_and_nothing_published()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "copy-page");
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "about\n  team\n  careers");
        var team = ids["/about/team"];
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, team), PagesApi.DocumentWithText("zz-canary-theirs")));
        var header = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header")).GetProperty("id").GetGuid();
        var mine = PagesApi.DocumentReferencing(header);

        var first = await CopyOkAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, team), mine, title: "Team (copy)");
        var second = await CopyOkAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, team), mine, title: "Team (copy)");

        Assert.Equal("Team (copy)", first.GetProperty("title").GetString());
        Assert.Equal("team-copy", first.GetProperty("slug").GetString());
        Assert.Equal("team-copy-2", second.GetProperty("slug").GetString());
        Assert.Equal(ids["/about"], first.GetProperty("parentId").GetGuid());

        // Directly after the original, in the order the copies were made.
        var paths = await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA);
        Assert.Equal(["/about", "/about/team", "/about/team-copy-2", "/about/team-copy", "/about/careers"], paths);

        var copyPath = PagesApi.PagePath(scenario.SiteA, first.GetProperty("id").GetGuid());
        var draft = await PagesApi.DraftOkAsync(scenario.Admin, copyPath);
        Assert.True(JsonElement.DeepEquals(JsonElement.Parse(mine), draft.GetProperty("document")));
        Assert.Equal("draft", first.GetProperty("publication").GetProperty("state").GetString());

        var origin = first.GetProperty("copyOrigin");
        Assert.Equal(team, origin.GetProperty("sourceId").GetGuid());
        Assert.Equal(LoadedAt, origin.GetProperty("sourceSavedAtUtc").GetDateTimeOffset());
        Assert.Equal("Test Administrator", origin.GetProperty("copiedBy").GetProperty("displayName").GetString());

        // The copy's usage rows were written with it: the header counts it.
        Assert.Equal(2, await scenario.ReadAsync(d => PagesScenario.Everything<ComponentUsage>(d).CountAsync(u => u.ComponentId == header)));

        // The original keeps the other member's version.
        Assert.Contains("zz-canary-theirs", (await PagesApi.DraftOkAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, team))).GetProperty("document").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publishing_the_original_never_publishes_the_copy()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "copy-page-publish");
        var page = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "pricing")).GetProperty("id").GetGuid();
        var path = PagesApi.PagePath(scenario.SiteA, page);
        var copy = await CopyOkAsync(scenario.Admin, path, PagesApi.DocumentWithText("zz-canary-mine"), title: "Pricing (copy)");

        var version = (await PagesApi.DraftOkAsync(scenario.Admin, path)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PublishingApi.PublishAsync(scenario.Admin, path, version));

        var copied = await PagesApi.OkAsync(await PagesApi.GetAsync(scenario.Admin, scenario.SiteA, copy.GetProperty("id").GetGuid()));
        Assert.Equal("draft", copied.GetProperty("publication").GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_component_copy_has_its_name_made_unique_is_used_by_no_page_and_records_its_origin()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "copy-component");
        var footer = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid();
        await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer (copy)");
        var path = PagesApi.ComponentPath(scenario.SiteA, footer);

        var copy = await CopyOkAsync(scenario.Admin, path, PagesApi.DocumentWithText("zz-canary-mine"), name: "Footer (copy)");

        Assert.Equal("Footer (copy) 2", copy.GetProperty("name").GetString());
        Assert.Equal(0, copy.GetProperty("usageCount").GetInt32());
        Assert.Equal(footer, copy.GetProperty("copyOrigin").GetProperty("sourceId").GetGuid());
        var draft = await PagesApi.DraftOkAsync(scenario.Admin, PagesApi.ComponentPath(scenario.SiteA, copy.GetProperty("id").GetGuid()));
        Assert.Contains("zz-canary-mine", draft.GetProperty("document").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_document_creates_nothing_and_the_create_capability_is_required()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "copy-refusals");
        var page = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "pricing")).GetProperty("id").GetGuid();
        var path = PagesApi.PagePath(scenario.SiteA, page);
        var footer = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid();
        var before = await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA);

        await PagesApi.RefusedAsync(
            await CopyAsync(scenario.Admin, path, """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"x","type":"nonsense","props":{},"children":[]}]}}""", title: "Pricing (copy)"),
            HttpStatusCode.BadRequest,
            "pages.invalid_document");
        Assert.Equal(before, await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA));

        // An Editor may save page drafts but not create pages or components (010).
        var editor = await scenario.UserAsync((scenario.SiteA, SystemRole.Editor));
        Assert.Equal(HttpStatusCode.Forbidden, (await CopyAsync(editor, path, PagesApi.DocumentWithText("x"), title: "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CopyAsync(editor, PagesApi.ComponentPath(scenario.SiteA, footer), PagesApi.DocumentWithText("x"), name: "x")).StatusCode);
        Assert.Equal(before, await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA));

        // Another site's page is not found.
        Assert.Equal(HttpStatusCode.NotFound, (await CopyAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteB, page), PagesApi.DocumentWithText("x"), title: "x")).StatusCode);
    }

    private static Task<HttpResponseMessage> CopyAsync(HttpClient client, string ownerPath, string document, string? title = null, string? name = null) =>
        client.PostAsJsonAsync(
            new Uri($"{ownerPath}/copies", UriKind.Relative),
            new { document = JsonElement.Parse(document), baseSavedAtUtc = LoadedAt, title, name });

    private static async Task<JsonElement> CopyOkAsync(HttpClient client, string ownerPath, string document, string? title = null, string? name = null) =>
        await PagesApi.OkAsync(await CopyAsync(client, ownerPath, document, title, name), HttpStatusCode.Created);
}
