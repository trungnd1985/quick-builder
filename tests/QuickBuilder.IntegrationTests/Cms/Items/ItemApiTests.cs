using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>Creating, reading and saving items through the API (015 US1, FR-001 to FR-005, FR-013, FR-014, SC-002).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemApiTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_value_of_every_type_is_read_back_exactly_as_saved()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-round-trip");
        var ann = ItemsApi.Id(await scenario.PersonAsync("Ann"));
        var bob = ItemsApi.Id(await scenario.PersonAsync("Bob"));
        var values = new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Hello world",
            ["f2"] = JsonNode.Parse("""{ "type": "doc", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "zz-canary-body" } ] } ] }"""),
            ["f3"] = 7,
            ["f4"] = true,
            ["f5"] = "2026-10-08",
            ["f6"] = new JsonObject { ["mediaId"] = scenario.Image.ToString() },
            ["f7"] = new JsonObject { ["mediaId"] = scenario.Pdf.ToString() },
            ["f8"] = "o2",
            ["f9"] = ann.ToString(),
            ["f10"] = new JsonArray(bob.ToString(), ann.ToString()),
        };

        var created = await ItemsApi.CreateOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Draft("hello-world", values, scenario.PostsSchemaRevision));

        Assert.Equal(1, ItemsApi.Revision(created));
        Assert.Equal("draft", created.GetProperty("status").GetString());
        Assert.Empty(ItemsApi.Problems(created.GetProperty("report")));
        Assert.False(created.GetProperty("attention").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("published").ValueKind);

        var read = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(created));
        Assert.True(JsonNode.DeepEquals(values, ItemsApi.Values(read)));
        Assert.Equal($"{PagesApi.CanaryMarker}Hello world", read.GetProperty("name").GetString());
        Assert.Equal("hello-world", read.GetProperty("slug").GetString());
        Assert.Equal(scenario.PostsSchemaRevision, read.GetProperty("schemaRevision").GetInt32());

        var events = (await scenario.Cms.OutboxAsync()).Where(e => e.Type.StartsWith("Cms.Item", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, events.Count(e => e.Type == "Cms.ItemCreated"));
        Assert.All(events, e => Assert.DoesNotContain(PagesApi.CanaryMarker, e.Payload, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("f3", "\"seven\"", "malformed")]
    [InlineData("f8", "\"o99\"", "unknown_option")]
    [InlineData("f99", "\"x\"", "unknown_field")]
    public async Task A_malformed_value_is_refused_against_its_field_and_nothing_is_stored(string fieldId, string json, string problem)
    {
        using var scenario = await ItemsScenario.CreateAsync(database, $"items-malformed-{problem}");
        var values = new JsonObject { ["f1"] = "zz-canary-title", [fieldId] = JsonNode.Parse(json) };

        var refused = await PagesApi.ProblemAsync(await ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Draft("post", values, scenario.PostsSchemaRevision)));

        Assert.Equal((HttpStatusCode.BadRequest, "cms.invalid_item"), (refused.Status, refused.Code));
        Assert.Equal([(fieldId, problem)], ItemsApi.Problems(refused.Body.GetProperty("report")));
        Assert.Equal(0, (await ItemsApi.ListOkAsync(scenario.Admin, scenario.Site, scenario.Posts)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task References_to_another_sites_item_or_a_missing_one_are_refused()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-foreign-reference");
        var otherSitePeople = await scenario.Cms.CollectionAsync("people", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "name", "text", settings: new { multiline = false, maxLength = 100 })),
        }, scenario.Cms.SiteB);
        var foreign = await ItemsApi.CreateOkAsync(scenario.Admin, scenario.Cms.SiteB, otherSitePeople.GetProperty("id").GetGuid(),
            ItemsApi.Draft("zed", new JsonObject { ["f1"] = "zz-canary-zed" }, otherSitePeople.GetProperty("schemaRevision").GetInt32()));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-t", ["f9"] = ItemsApi.Id(foreign).ToString() }, scenario.PostsSchemaRevision)));

        Assert.Equal([("f9", "reference_missing")], ItemsApi.Problems(refused.Body.GetProperty("report")));

        // Looked up from site A, site B's item is missing (Principle V).
        var lookup = await PagesApi.OkAsync(await ItemsApi.LookupAsync(scenario.Admin, scenario.Site, [ItemsApi.Id(foreign)]));
        Assert.Equal(0, lookup.GetProperty("items").GetArrayLength());
        Assert.Equal(ItemsApi.Id(foreign), lookup.GetProperty("missing")[0].GetGuid());
    }

    [Fact]
    public async Task An_incomplete_draft_is_stored_with_its_report_and_needs_attention()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-incomplete");
        var values = new JsonObject { ["f3"] = -5 };

        var created = await ItemsApi.CreateOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Draft("untitled", values, scenario.PostsSchemaRevision));

        Assert.True(created.GetProperty("attention").GetBoolean());
        Assert.Equal([("f1", "missing_required"), ("f3", "out_of_range")], ItemsApi.Problems(created.GetProperty("report")));
        var listed = (await ItemsApi.ListOkAsync(scenario.Admin, scenario.Site, scenario.Posts)).GetProperty("items")[0];
        Assert.True(listed.GetProperty("attention").GetBoolean());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task Slugs_are_unique_in_the_collection_ignoring_case_and_the_next_free_one_is_suggested()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-slugs");
        var first = await scenario.PostAsync("hello-world");

        var taken = await PagesApi.ProblemAsync(await ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts,
            ItemsApi.Draft("hello-world", new JsonObject { ["f1"] = "zz-canary-t" }, scenario.PostsSchemaRevision)));
        Assert.Equal((HttpStatusCode.Conflict, "cms.item_slug_taken"), (taken.Status, taken.Code));
        Assert.Equal("hello-world-2", taken.Text("suggestion"));

        var invalid = await PagesApi.ProblemAsync(await ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts,
            ItemsApi.Draft("HELLO-WORLD", new JsonObject { ["f1"] = "zz-canary-t" }, scenario.PostsSchemaRevision)));
        Assert.Equal([("slug", "invalid_slug")], ItemsApi.Problems(invalid.Body.GetProperty("report")));

        var check = await ItemsApi.SlugAsync(scenario.Admin, scenario.Site, scenario.Posts, "hello-world");
        Assert.False(check.GetProperty("available").GetBoolean());
        Assert.Equal("hello-world-2", check.GetProperty("suggestion").GetString());

        var own = await ItemsApi.SlugAsync(scenario.Admin, scenario.Site, scenario.Posts, "hello-world", ItemsApi.Id(first));
        Assert.True(own.GetProperty("available").GetBoolean());

        // Another collection may use the same slug.
        await scenario.PersonAsync("Hello", "hello-world");
    }

    [Fact]
    public async Task A_save_based_on_an_old_revision_or_an_old_schema_is_refused_but_not_after_a_details_change()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-conflicts");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);
        var saved = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-second" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
        Assert.Equal(2, ItemsApi.Revision(saved));

        var stale = await PagesApi.ProblemAsync(await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-third" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item))));
        Assert.Equal("cms.item_conflict", stale.Code);
        Assert.Equal("2", stale.Text("currentRevision"));

        // Renaming the collection leaves the schema as it was: the form is still right.
        var collection = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var renamed = await CmsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts,
            CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: "zz-canary-Articles"));
        Assert.Equal(scenario.PostsSchemaRevision, renamed.GetProperty("schemaRevision").GetInt32());
        saved = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-third" }, scenario.PostsSchemaRevision, ItemsApi.Revision(saved)));

        // Renaming a field changes the schema: a form built before it is out of date.
        var schema = CmsApi.Schema(renamed);
        CmsApi.Fields(schema)[0]!["name"] = "Headline";
        var changed = await CmsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, CmsApi.SaveBody(renamed, schema));
        var outdated = await PagesApi.ProblemAsync(await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-fourth" }, scenario.PostsSchemaRevision, ItemsApi.Revision(saved))));
        Assert.Equal("cms.schema_changed", outdated.Code);
        Assert.Equal(changed.GetProperty("schemaRevision").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), outdated.Text("schemaRevision"));
    }

    [Fact]
    public async Task Bodies_over_one_mebibyte_and_duplicate_properties_are_refused()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-body-limits");

        // A sized body, as browsers send: refused on its declared length, before it is read.
        var huge = new JsonObject { ["f1"] = new string('x', 1_100_000) };
        using var hugeContent = new StringContent(JsonSerializer.Serialize(ItemsApi.Draft("huge", huge, scenario.PostsSchemaRevision)), Encoding.UTF8, "application/json");
        var tooLarge = await PagesApi.ProblemAsync(await scenario.Admin.PostAsync(new Uri(ItemsApi.ItemsPath(scenario.Site, scenario.Posts), UriKind.Relative), hugeContent));
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "cms.item_too_large"), (tooLarge.Status, tooLarge.Code));

        var duplicate = $$"""{ "slug": "dup", "schemaRevision": {{scenario.PostsSchemaRevision}}, "values": { "schemaVersion": 1, "values": { "f1": "a", "f1": "b" } } }""";
        using var content = new StringContent(duplicate, Encoding.UTF8, "application/json");
        var refused = await PagesApi.ProblemAsync(await scenario.Admin.PostAsync(new Uri(ItemsApi.ItemsPath(scenario.Site, scenario.Posts), UriKind.Relative), content));
        Assert.Equal("cms.invalid_item", refused.Code);
    }

    [Fact]
    public async Task Every_site_role_may_write_items_and_a_user_without_a_role_finds_nothing()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-roles");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);

        foreach (var role in new[] { SystemRole.Editor, SystemRole.Designer })
        {
            using var member = await scenario.Cms.UserAsync((scenario.Site, role));
            var created = await ItemsApi.CreateOkAsync(member, scenario.Site, scenario.Posts,
                ItemsApi.Draft($"by-{role.ToString().ToLowerInvariant()}", new JsonObject { ["f1"] = "zz-canary-t" }, scenario.PostsSchemaRevision));
            await PagesApi.OkAsync(await ItemsApi.PublishAsync(member, scenario.Site, scenario.Posts, ItemsApi.Id(created), ItemsApi.Revision(created)));
        }

        using var stranger = await scenario.Cms.UserAsync((scenario.Cms.SiteB, SystemRole.Editor));
        var responses = new[]
        {
            await ItemsApi.ListAsync(stranger, scenario.Site, scenario.Posts),
            await ItemsApi.GetAsync(stranger, scenario.Site, scenario.Posts, id),
            await ItemsApi.CreateAsync(stranger, scenario.Site, scenario.Posts, ItemsApi.Draft("x", new JsonObject(), 1)),
            await ItemsApi.SaveAsync(stranger, scenario.Site, scenario.Posts, id, ItemsApi.Draft("x", new JsonObject(), 1, 1)),
            await ItemsApi.PublishAsync(stranger, scenario.Site, scenario.Posts, id, 1),
            await ItemsApi.DeleteAsync(stranger, scenario.Site, scenario.Posts, id, 1),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }
}
