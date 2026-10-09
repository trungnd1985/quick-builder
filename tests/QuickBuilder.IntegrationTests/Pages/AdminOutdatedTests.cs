using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// An admin tab opened before 035 cannot save over content that uses the 035 formats, so it
/// never silently drops them; it can still save everything else (035 FR-020, research RT-13).
/// </summary>
/// <remarks>
/// An old tab sends no <c>X-QB-Rich-Text</c> header. The stored drafts below are given a 035
/// format directly in the database, so the test does not depend on any format being editable.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AdminOutdatedTests(DatabaseFixture database)
{
    private const string Extended = """{"type":"doc","content":[{"type":"paragraph","attrs":{"align":"center"},"content":[{"type":"text","text":"Centred"}]}]}""";

    private const string Plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Plain"}]}]}""";

    private static string Page(string richText) =>
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{},"children":[{"id":"r","type":"rich-text","props":{"content":CONTENT},"children":[]}]}]}}"""
            .Replace("CONTENT", richText, StringComparison.Ordinal);

    [Fact]
    public async Task A_page_or_component_draft_with_035_formats_refuses_a_save_from_an_old_admin()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "admin-outdated-pages");
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "home*\nnews"))["/news"]);
        var componentId = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid();
        var component = PagesApi.ComponentPath(scenario.SiteA, componentId);

        await StoreAsync(scenario, $"UPDATE pages.page_drafts SET document = {Page(Extended)}::jsonb");
        await StoreAsync(scenario, $"UPDATE pages.component_drafts SET document = {Page(Extended)}::jsonb");
        var old = await OldAdminAsync(scenario.App);

        foreach (var path in new[] { page, component })
        {
            var refused = await PagesApi.ProblemAsync(await PagesApi.SaveCurrentDraftAsync(old, path, Page(Plain)));
            Assert.Equal((HttpStatusCode.Conflict, "pages.admin_outdated"), (refused.Status, refused.Code));
            Assert.Contains("\"align\":\"center\"", (await PagesApi.DraftOkAsync(scenario.Admin, path)).GetRawText(), StringComparison.Ordinal);

            using var lowerLevel = await PagesApi.SaveDraftWithHeaderAsync(old, path, Page(Plain), RichTextRules.ClientHeader, "1");
            Assert.Equal(HttpStatusCode.Conflict, lowerLevel.StatusCode);

            await PublishingApi.SaveAsync(scenario.Admin, path, Page(Plain));
        }
    }

    [Fact]
    public async Task An_old_admin_still_saves_content_that_never_used_the_035_formats()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "admin-outdated-plain");
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "home*\nnews"))["/news"]);
        var old = await OldAdminAsync(scenario.App);

        await PublishingApi.SaveAsync(old, page, Page(Plain));
        await PublishingApi.SaveAsync(old, page, Page(Plain).Replace("Plain", "Changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_item_draft_with_035_formats_refuses_a_save_from_an_old_admin_but_still_duplicates()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "admin-outdated-items");
        var created = await scenario.PostAsync("post", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}post", ["f2"] = JsonNode.Parse(Plain) });
        var id = ItemsApi.Id(created);
        await scenario.Cms.WithDatabaseAsync(cms => cms.Database.ExecuteSqlAsync(
            $"UPDATE cms.items SET draft = jsonb_set(draft, '{{values,f2}}', {Extended}::jsonb) WHERE id = {id}"));
        var save = ItemsApi.Draft("post", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}post", ["f2"] = JsonNode.Parse(Plain) }, scenario.PostsSchemaRevision, ItemsApi.Revision(created));

        var old = await OldAdminAsync(scenario.Cms.App);
        var refused = await PagesApi.ProblemAsync(await ItemsApi.SaveAsync(old, scenario.Site, scenario.Posts, id, save));
        Assert.Equal((HttpStatusCode.Conflict, "cms.admin_outdated"), (refused.Status, refused.Code));
        Assert.Contains("\"align\":\"center\"", ItemsApi.Values(await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id))["f2"]!.ToJsonString(), StringComparison.Ordinal);

        // Duplicating copies on the server, so an old admin loses nothing by it.
        var copy = await PagesApi.OkAsync(await ItemsApi.PostAsync(old, scenario.Site, scenario.Posts, id, "duplicate"), HttpStatusCode.Created);
        Assert.True(JsonNode.DeepEquals(ItemsApi.Values(copy)["f2"], JsonNode.Parse(Extended)));

        await PagesApi.OkAsync(await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id, save));
    }

    /// <summary>An admin from before 035: signed in, but sending no rich text level.</summary>
    private static async Task<HttpClient> OldAdminAsync(QuickBuilderApp app)
    {
        var client = await app.CreateAdminClientAsync();
        client.DefaultRequestHeaders.Remove(RichTextRules.ClientHeader);
        return client;
    }

    private static async Task StoreAsync(PagesScenario scenario, FormattableString sql)
    {
        await using var scope = scenario.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PagesDbContext>().Database.ExecuteSqlAsync(sql);
    }
}
