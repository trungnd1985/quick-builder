using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// A rich text field may limit its formats (035 FR-022 to FR-025, clarification Q1): an item
/// using a format its field does not allow saves, needs attention and does not publish;
/// narrowing the formats is a reported change, and widening them clears the attention.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemRichTextFormatsTests(DatabaseFixture database)
{
    private const string Media = "0192f5a1-0000-7000-8000-000000000010";

    private static JsonNode Body(params string[] blocks) =>
        JsonNode.Parse("""{"type":"doc","content":[BLOCKS]}""".Replace("BLOCKS", string.Join(",", blocks), StringComparison.Ordinal))!;

    private const string Paragraph = """{"type":"paragraph","content":[{"type":"text","text":"Plain"}]}""";
    private const string Table = """{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}""";
    private const string Image = """{"type":"image","attrs":{"media":{"mediaId":"MEDIA"},"decorative":true}}""";

    private static async Task<(CmsScenario Scenario, JsonElement Collection)> CollectionAsync(DatabaseFixture database, string label, object? formats)
    {
        var scenario = await CmsScenario.CreateAsync(database, label);
        var collection = await scenario.CollectionAsync("posts", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", required: true, settings: new { multiline = false, maxLength = 100 }),
                CmsApi.Field("~2", "body", "richText", settings: formats is null ? null : new { formats })),
        });
        return (scenario, collection);
    }

    private static Task<JsonElement> ItemAsync(CmsScenario scenario, JsonElement collection, string slug, JsonNode body) =>
        ItemsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, collection.GetProperty("id").GetGuid(), ItemsApi.Draft(
            slug, new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}{slug}", ["f2"] = body }, collection.GetProperty("schemaRevision").GetInt32()));

    [Fact]
    public async Task An_item_using_a_format_its_field_does_not_allow_saves_needs_attention_and_does_not_publish()
    {
        var (scenario, collection) = await CollectionAsync(database, "rt-formats-item", new { images = false });
        using var _ = scenario;
        var media = (await QuickBuilder.IntegrationTests.Media.MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA)).Id.ToString();

        var item = await ItemAsync(scenario, collection, "post", Body(Paragraph, Image.Replace("MEDIA", media, StringComparison.Ordinal)));

        Assert.True(item.GetProperty("attention").GetBoolean());
        var problem = Assert.Single(item.GetProperty("report").GetProperty("problems").EnumerateArray());
        Assert.Equal(("f2", "format_not_allowed", "images"), (problem.GetProperty("fieldId").GetString(), problem.GetProperty("problem").GetString(), problem.GetProperty("format").GetString()));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.SiteA, collection.GetProperty("id").GetGuid(), ItemsApi.Id(item), ItemsApi.Revision(item)));
        Assert.Equal((HttpStatusCode.Conflict, "cms.item_needs_attention"), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task Narrowing_the_formats_is_reported_with_the_items_it_affects_and_widening_clears_them()
    {
        var (scenario, collection) = await CollectionAsync(database, "rt-formats-narrow", formats: null);
        using var _ = scenario;
        var id = collection.GetProperty("id").GetGuid();
        var withTables = new List<Guid>();
        for (var n = 0; n < 3; n++)
        {
            withTables.Add(ItemsApi.Id(await ItemAsync(scenario, collection, $"table-{n}", Body(Paragraph, Table))));
        }

        var plain = ItemsApi.Id(await ItemAsync(scenario, collection, "plain", Body(Paragraph)));

        var narrowed = CmsApi.Schema(collection);
        CmsApi.Fields(narrowed)[1]!["formats"] = new JsonObject { ["tables"] = false };
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, narrowed));
        var change = preview.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "settingsChanged");
        Assert.Equal("reported", change.GetProperty("rule").GetString());
        Assert.Equal(3, change.GetProperty("counts").GetProperty("nowInvalid").GetInt32());

        collection = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, narrowed));
        foreach (var item in withTables)
        {
            Assert.True((await ItemsApi.GetOkAsync(scenario.Admin, scenario.SiteA, id, item)).GetProperty("attention").GetBoolean());
        }

        Assert.False((await ItemsApi.GetOkAsync(scenario.Admin, scenario.SiteA, id, plain)).GetProperty("attention").GetBoolean());

        var widened = CmsApi.Schema(collection);
        CmsApi.Fields(widened)[1]!.AsObject().Remove("formats");
        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, widened));
        foreach (var item in withTables)
        {
            Assert.False((await ItemsApi.GetOkAsync(scenario.Admin, scenario.SiteA, id, item)).GetProperty("attention").GetBoolean());
        }
    }
}
