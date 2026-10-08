using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Rich text written before 035 stays valid and renders byte for byte as it did (035 SC-001,
/// FR-004; constitution Principle III).
/// </summary>
/// <remarks>
/// 035 adds formats without a migration, so "upgrading" means that content written with only
/// the 015 formats meets the new code. The documents below use every 015 rich text feature; the
/// golden files under <c>tests/fixtures/rendering/upgrade-015/</c> were recorded with the 015
/// renderer, before any 035 change. Only the Rich Text blocks' markup is compared: the page
/// around it embeds a hash of the shared stylesheet, which 035 extends. Items aren't rendered
/// until #16, so an item's value is checked for validity and for coming back unchanged. Set
/// <c>QB_UPDATE_GOLDEN=1</c> to rewrite the golden files, and only ever for a deliberate change.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed partial class UpgradeFrom015Tests(DatabaseFixture database)
{
    /// <summary>Every 015 format: paragraphs, nested and numbered lists, breaks, marks and both link kinds.</summary>
    private static string RichText(Guid linkedPageId) => """
        {"type":"doc","content":[
          {"type":"paragraph","content":[
            {"type":"text","text":"Plain <text> & \"quotes\" "},
            {"type":"text","text":"bold","marks":[{"type":"bold"}]},
            {"type":"text","text":" both","marks":[{"type":"bold"},{"type":"italic"}]},
            {"type":"hardBreak"},
            {"type":"text","text":"a page","marks":[{"type":"link","attrs":{"to":{"type":"page","pageId":"{linkedPageId}"}}}]},
            {"type":"text","text":" and ","marks":[{"type":"italic"}]},
            {"type":"text","text":"an address","marks":[{"type":"italic"},{"type":"link","attrs":{"to":{"type":"url","href":"https://example.com/a?b=1&c=2"}}}]}]},
          {"type":"paragraph"},
          {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"One"}]},
              {"type":"orderedList","attrs":{"start":3},"content":[
                {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Three"}]}]}]}]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Two"}]}]}]},
          {"type":"orderedList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"First"}]}]}]}
        ]}
        """.Replace("{linkedPageId}", linkedPageId.ToString(), StringComparison.Ordinal);

    /// <summary>A Rich Text block near the 20,000-character limit (19,998).</summary>
    private static readonly string LongRichText = JsonSerializer.Serialize(new
    {
        type = "doc",
        content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = string.Concat(Enumerable.Repeat("Long text. ", 1_818)) } } } },
    });

    [Fact]
    public async Task Pages_and_components_with_015_rich_text_render_exactly_as_before()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "upgrade-015-pages").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "home*\nlegacy").ConfigureAwait(true);
        var home = ids["/"];

        var component = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Legacy footer").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var componentPath = PagesApi.ComponentPath(scenario.SiteA, component);
        await PublishingApi.SaveAsync(scenario.Admin, componentPath, Document(null, ("cr", RichText(home), "end"))).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(scenario.Admin, componentPath).ConfigureAwait(true);

        var pagePath = PagesApi.PagePath(scenario.SiteA, ids["/legacy"]);
        await PublishingApi.SaveAsync(scenario.Admin, pagePath, Document(component, ("r1", RichText(home), "start"), ("r2", LongRichText, "center"))).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(scenario.Admin, pagePath).ConfigureAwait(true);

        var response = await scenario.App.CreateClientForHost(PagesScenario.HostA).GetAsync(new Uri("/legacy", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var blocks = string.Join("\n", RichTextBlock().Matches(await response.Content.ReadAsStringAsync().ConfigureAwait(true)).Select(m => m.Value)) + "\n";

        Assert.Equal(3, RichTextBlock().Count(blocks));
        AssertGolden("page.html", blocks
            .Replace(home.ToString(), "{home}", StringComparison.Ordinal)
            .Replace(component.ToString("N"), "{component}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_item_with_015_rich_text_is_valid_and_comes_back_unchanged()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "upgrade-015-items").ConfigureAwait(true);
        var value = JsonNode.Parse(RichText(Guid.CreateVersion7()))!;

        var created = await scenario.PostAsync("legacy", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}legacy", ["f2"] = value.DeepClone() }).ConfigureAwait(true);
        var published = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(created), ItemsApi.Revision(created)).ConfigureAwait(true);
        var loaded = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(created)).ConfigureAwait(true);

        Assert.Empty(ItemsApi.Problems(loaded.GetProperty("report")));
        Assert.True(JsonNode.DeepEquals(value, ItemsApi.Values(loaded)["f2"]));
        Assert.True(JsonNode.DeepEquals(value, ItemsApi.Values(published, "published")["f2"]));
    }

    /// <summary>A page or component document: an optional component, then one Section of Rich Text blocks.</summary>
    private static string Document(Guid? componentId, params (string Id, string Content, string Align)[] blocks)
    {
        var reference = componentId is { } id ? """{"id":"ref","type":"component-ref","props":{"componentId":"ID"},"children":[]},""".Replace("ID", id.ToString(), StringComparison.Ordinal) : string.Empty;
        var children = string.Join(",", blocks.Select(b => """{"id":"ID","type":"rich-text","props":{"content":CONTENT,"align":"ALIGN"},"children":[]}""".Replace("ID", b.Id, StringComparison.Ordinal).Replace("ALIGN", b.Align, StringComparison.Ordinal).Replace("CONTENT", b.Content, StringComparison.Ordinal)));
        return """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[REF{"id":"s","type":"section","props":{},"children":[CHILDREN]}]}}""".Replace("REF", reference, StringComparison.Ordinal).Replace("CHILDREN", children, StringComparison.Ordinal);
    }

    private static void AssertGolden(string name, string actual, [CallerFilePath] string here = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        var golden = Path.Combine(root, "tests", "fixtures", "rendering", "upgrade-015", name);

        if (Environment.GetEnvironmentVariable("QB_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, actual);
            return;
        }

        Assert.True(File.Exists(golden), $"No golden file {name}. Record it with QB_UPDATE_GOLDEN=1 on the 015 renderer only.");
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>A Rich Text block's markup: its wrapper and its body, which holds no other div.</summary>
    [GeneratedRegex("<div class=\"qb-rich-text qb-align--[a-z]+\"[^>]*>.*?</div></div>", RegexOptions.Singleline)]
    private static partial Regex RichTextBlock();
}
