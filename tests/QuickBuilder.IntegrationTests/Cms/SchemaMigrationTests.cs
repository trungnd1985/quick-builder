using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Schema changes after items exist: what the preview says is what is stored, draft and
/// published alike (014 US3, US4, SC-004, FR-023 … FR-031).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SchemaMigrationTests(DatabaseFixture database)
{
    private static JsonObject Text(string id, string key, bool required = false, bool multiline = false, int maxLength = 256) =>
        CmsApi.Field(id, key, "text", required, new { multiline, maxLength });

    private static JsonObject Schema(params JsonObject[] fields) => new() { ["schemaVersion"] = 1, ["fields"] = new JsonArray(fields) };

    private static JsonElement Change(JsonElement preview, string kind) =>
        preview.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == kind);

    private static int Count(JsonElement change, string outcome) =>
        change.GetProperty("counts").TryGetProperty(outcome, out var count) ? count.GetInt32() : 0;

    private static JsonNode? Value(string document, string fieldId) =>
        JsonNode.Parse(document)!["values"]![fieldId];

    [Fact]
    public async Task An_added_optional_field_changes_nothing_and_needs_no_preview()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-add-optional");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title")));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 10, published: true);
        var before = await scenario.ItemsAsync(id);

        var added = CmsApi.Schema(collection);
        CmsApi.Fields(added).Add(Text("~1", "subtitle"));
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, added));
        Assert.Equal("free", Change(preview, "fieldAdded").GetProperty("rule").GetString());

        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, added));

        Assert.Equal(before, await scenario.ItemsAsync(id));
    }

    [Fact]
    public async Task Fill_in_values_reach_every_item_lacking_one_in_draft_and_published_copies()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-fill-in");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title")));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 3, published: true);
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 2);

        var added = CmsApi.Schema(collection);
        CmsApi.Fields(added).Add(CmsApi.Field("~1", "featured", "boolean"));
        CmsApi.Fields(added).Add(CmsApi.Field("~2", "language", "option", required: true, settings: new { options = new[] { new { id = "~3", label = "English" }, new { id = "~4", label = "Vietnamese" } } }));
        var fillIns = new Dictionary<string, object> { ["~1"] = true, ["~2"] = "~3" };

        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, added, fillIns));
        var language = preview.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "language" && c.GetProperty("kind").GetString() == "fieldAdded");
        Assert.Equal(("reported", 5, true), (language.GetProperty("rule").GetString(), Count(language, "filled"), language.GetProperty("fillInAllowed").GetBoolean()));
        Assert.False(preview.GetProperty("requiresConfirmation").GetBoolean());

        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, added, fillIns));
        Assert.Equal(("f2", "f3", "o1"), (saved.GetProperty("idMap").GetProperty("~1").GetString(), saved.GetProperty("idMap").GetProperty("~2").GetString(), saved.GetProperty("idMap").GetProperty("~3").GetString()));

        var items = await scenario.ItemsAsync(id);
        Assert.All(items, i => Assert.Equal(("true", "\"o1\""), (Value(i.Draft, "f2")!.ToJsonString(), Value(i.Draft, "f3")!.ToJsonString())));
        Assert.Equal(3, items.Count(i => i.Published is not null && Value(i.Published, "f3")!.GetValue<string>() == "o1"));
        Assert.Equal(2, items.Count(i => i.Published is null));
    }

    [Fact]
    public async Task Turning_required_on_without_a_fill_in_counts_the_items_still_missing_and_saves()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-required");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title"), Text("~2", "summary")));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 6, valuesFor: (field, n) => field.Key == "summary" && n % 3 == 0 ? null : ItemSeed.ValueFor(field, n));
        var before = await scenario.ItemsAsync(id);

        var required = CmsApi.Schema(collection);
        CmsApi.Fields(required)[1]!["required"] = true;
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, required));
        Assert.Equal(2, Count(Change(preview, "requiredChanged"), "stillMissing"));

        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, required));

        Assert.Equal(before, await scenario.ItemsAsync(id));
    }

    [Fact]
    public async Task Removing_a_field_with_values_needs_confirmation_and_then_deletes_them_from_both_copies()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-remove");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title"), CmsApi.Field("~2", "minutes", "number", settings: new { decimals = 0 })));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 7, published: true, valuesFor: (field, n) => field.Key == "minutes" && n < 2 ? null : ItemSeed.ValueFor(field, n));

        var removed = CmsApi.Schema(collection);
        CmsApi.Fields(removed).RemoveAt(1);

        var refused = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removed)));
        Assert.Equal((HttpStatusCode.Conflict, "cms.confirmation_required"), (refused.Status, refused.Code));
        var preview = refused.Body.GetProperty("preview");
        Assert.True(preview.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal(5, Count(Change(preview, "fieldRemoved"), "cleared"));
        Assert.Equal(5, Change(preview, "fieldRemoved").GetProperty("examples").GetArrayLength());

        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removed, digest: preview.GetProperty("digest").GetString()));

        Assert.All(await scenario.ItemsAsync(id), i =>
        {
            Assert.Null(Value(i.Draft, "f2"));
            Assert.Null(Value(i.Published!, "f2"));
        });
    }

    [Fact]
    public async Task Removing_an_option_clears_only_the_items_that_chose_it()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-remove-option");
        var category = CmsApi.Field("~1", "category", "option", settings: new { options = new[] { new { id = "~2", label = "News" }, new { id = "~3", label = "Guides" }, new { id = "~4", label = "Releases" } } });
        var collection = await scenario.CollectionAsync("posts", Schema(category));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 9);

        var schema = CmsApi.Schema(collection);
        CmsApi.Fields(schema)[0]!["options"]!.AsArray().RemoveAt(2);
        var (preview, _) = await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.SiteA, collection, schema);

        Assert.Equal(3, Count(Change(preview, "optionRemoved"), "cleared"));
        var items = await scenario.ItemsAsync(id);
        Assert.Equal(3, items.Count(i => Value(i.Draft, "f1") is null));
        Assert.DoesNotContain(items, i => Value(i.Draft, "f1")?.GetValue<string>() == "o3");
    }

    [Fact]
    public async Task The_field_that_names_items_cannot_be_removed()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-remove-title");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title"), Text("~2", "summary")));

        var schema = CmsApi.Schema(collection);
        CmsApi.Fields(schema).RemoveAt(0);
        var problem = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, collection.GetProperty("id").GetGuid(), CmsApi.SaveBody(collection, schema)));

        Assert.Equal(("cms.invalid_schema", "invalid_title_field"), (problem.Code, problem.Text("rule")));
    }

    [Fact]
    public async Task A_fill_in_for_a_type_that_takes_none_or_for_an_unchanged_field_is_refused()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-bad-fill-in");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title")));
        var id = collection.GetProperty("id").GetGuid();

        var added = CmsApi.Schema(collection);
        CmsApi.Fields(added).Add(CmsApi.Field("~1", "body", "richText"));
        var rich = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, added, new Dictionary<string, object> { ["~1"] = "x" })));
        Assert.Equal(("cms.invalid_fill_in", "/fillIns/~01"), (rich.Code, rich.Text("pointer")));

        var unchanged = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, CmsApi.Schema(collection), new Dictionary<string, object> { ["f1"] = "x" })));
        Assert.Equal("cms.invalid_fill_in", unchanged.Code);

        var wrongType = CmsApi.Schema(collection);
        CmsApi.Fields(wrongType).Add(CmsApi.Field("~1", "minutes", "number", settings: new { decimals = 0 }));
        var notNumber = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, wrongType, new Dictionary<string, object> { ["~1"] = "seven" })));
        Assert.Equal("cms.invalid_fill_in", notNumber.Code);
    }

    [Fact]
    public async Task Text_to_number_stores_exactly_what_the_preview_counted()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-text-number");
        var collection = await scenario.CollectionAsync("posts", Schema(Text("~1", "title"), Text("~2", "minutes")));
        var id = collection.GetProperty("id").GetGuid();
        string?[] minutes = ["5", "12.5", "about 7", null];
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 4, published: true, valuesFor: (field, n) =>
            field.Key == "minutes" ? (minutes[n] is { } m ? JsonValue.Create(m) : null) : ItemSeed.ValueFor(field, n));

        var schema = CmsApi.Schema(collection);
        CmsApi.Fields(schema)[1] = CmsApi.Field("f2", "minutes", "number", settings: new { decimals = 0 });
        var (preview, _) = await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.SiteA, collection, schema);

        var change = Change(preview, "typeChanged");
        Assert.Equal(("lossy", 1, 1, 1), (change.GetProperty("rule").GetString(), Count(change, "converted"), Count(change, "partlyLost"), Count(change, "cleared")));

        var stored = (await scenario.ItemsAsync(id)).Select(i => (Value(i.Draft, "f2")?.ToJsonString(), Value(i.Published!, "f2")?.ToJsonString())).ToList();
        // Item ids made in one millisecond do not sort in the order they were made, so compare as a set.
        Assert.Equal([(null, null), (null, null), ("13", "13"), ("5", "5")], stored.Order());
    }

    /// <summary>
    /// Every type change contract §7 allows: the preview counts every item that held a value,
    /// and exactly the items it counts as cleared have lost theirs (SC-004).
    /// </summary>
    [Fact]
    public async Task Every_allowed_type_change_clears_exactly_what_its_preview_counted()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-type-matrix");
        var image = await MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA);
        var pdf = await LibraryApi.UploadNamedAsync(scenario.Admin, scenario.SiteA, "doc.pdf", SampleFiles.Pdf, "application/pdf");
        var a = Guid.CreateVersion7().ToString();
        var b = Guid.CreateVersion7().ToString();
        var people = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "people")).GetProperty("id").GetGuid();
        var rich = FieldConversion.TextToRichText("Hello");
        var options = new { options = new[] { new { id = "~10", label = "News" } } };

        (string From, object FromSettings, string To, object ToSettings, JsonNode?[] Values)[] cases =
        [
            ("text", new { multiline = true, maxLength = 5000 }, "richText", new { }, ["one\ntwo", "x"]),
            ("richText", new { }, "text", new { multiline = false, maxLength = 256 }, [rich, FieldConversion.TextToRichText("")]),
            ("number", new { decimals = 2 }, "text", new { multiline = false, maxLength = 256 }, [7.5m, 3]),
            ("text", new { multiline = false, maxLength = 256 }, "number", new { decimals = 0 }, ["5", "nope"]),
            ("date", new { withTime = false }, "text", new { multiline = false, maxLength = 256 }, ["2026-10-07"]),
            ("text", new { multiline = false, maxLength = 256 }, "date", new { withTime = true }, ["2026-10-07", "soon"]),
            ("option", options, "text", new { multiline = false, maxLength = 256 }, ["o1"]),
            ("text", new { multiline = false, maxLength = 256 }, "option", options, ["news", "events"]),
            ("boolean", new { }, "text", new { multiline = false, maxLength = 256 }, [true, false]),
            ("image", new { }, "file", new { accept = "any" }, [new JsonObject { ["mediaId"] = image.Id.ToString() }]),
            ("file", new { accept = "any" }, "image", new { }, [new JsonObject { ["mediaId"] = image.Id.ToString() }, new JsonObject { ["mediaId"] = pdf.Id.ToString() }]),
            ("reference", new { target = people }, "multiReference", new { target = people }, [a]),
            ("multiReference", new { target = people }, "reference", new { target = people }, [new JsonArray(a, b), new JsonArray(b)]),
        ];

        foreach (var (from, fromSettings, to, toSettings, values) in cases)
        {
            var label = $"{from}→{to}";
            var collection = await scenario.CollectionAsync(
                $"{from.ToLowerInvariant()}-to-{to.ToLowerInvariant()}",
                Schema(Text("~1", "title"), CmsApi.Field("~2", "subject", from, settings: fromSettings)));
            var id = collection.GetProperty("id").GetGuid();
            await ItemSeed.AddAsync(scenario, scenario.SiteA, id, values.Length + 1, published: true, valuesFor: (field, n) =>
                field.Key != "subject" ? ItemSeed.ValueFor(field, n) : n < values.Length ? values[n]?.DeepClone() : null);
            var before = await scenario.ItemsAsync(id);

            var schema = CmsApi.Schema(collection);
            CmsApi.Fields(schema)[1] = CmsApi.Field("f2", "subject", to, settings: toSettings);
            var (preview, _) = await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.SiteA, collection, schema);

            var change = Change(preview, "typeChanged");
            var counted = change.GetProperty("counts").EnumerateObject().Sum(c => c.Value.GetInt32());
            Assert.True(values.Length == counted, $"{label}: counted {counted} of {values.Length}");

            var after = await scenario.ItemsAsync(id);
            var lost = before.Zip(after).Count(p => Value(p.First.Draft, "f2") is not null && Value(p.Second.Draft, "f2") is null);
            Assert.True(Count(change, "cleared") == lost, $"{label}: preview cleared {Count(change, "cleared")}, stored lost {lost}");
            Assert.True(after.All(i => Value(i.Draft, "f2")?.ToJsonString() == Value(i.Published!, "f2")?.ToJsonString()), $"{label}: draft and published differ");
        }
    }

    [Fact]
    public async Task Narrowing_what_a_file_field_accepts_keeps_values_and_counts_those_now_invalid()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-accept");
        var image = await MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA);
        var pdf = await LibraryApi.UploadNamedAsync(scenario.Admin, scenario.SiteA, "doc.pdf", SampleFiles.Pdf, "application/pdf");
        var gone = Guid.CreateVersion7();
        var collection = await scenario.CollectionAsync("posts", Schema(CmsApi.Field("~1", "attachment", "file", settings: new { accept = "any" })));
        var id = collection.GetProperty("id").GetGuid();
        Guid[] media = [image.Id, pdf.Id, gone];
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 3, valuesFor: (_, n) => new JsonObject { ["mediaId"] = media[n].ToString() });
        var before = await scenario.ItemsAsync(id);

        var schema = CmsApi.Schema(collection);
        CmsApi.Fields(schema)[0]!["accept"] = "documents";
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, schema));
        var change = Change(preview, "settingsChanged");
        Assert.Equal(("reported", 1, 1, 1), (change.GetProperty("rule").GetString(), Count(change, "nowInvalid"), Count(change, "kept"), Count(change, "missing")));

        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, schema));
        Assert.Equal(before, await scenario.ItemsAsync(id));
    }

    [Fact]
    public async Task Forbidden_changes_are_refused_while_values_exist_and_allowed_on_an_empty_field()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-forbidden");
        var people = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "people")).GetProperty("id").GetGuid();
        var teams = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "teams")).GetProperty("id").GetGuid();
        var collection = await scenario.CollectionAsync("posts", Schema(
            CmsApi.Field("~1", "cover", "image"),
            CmsApi.Field("~2", "author", "reference", settings: new { target = people }),
            CmsApi.Field("~3", "empty", "image")));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 2, valuesFor: (field, n) => field.Key == "empty" ? null : ItemSeed.ValueFor(field, n));

        var toBoolean = CmsApi.Schema(collection);
        CmsApi.Fields(toBoolean)[0] = CmsApi.Field("f1", "cover", "boolean");
        var type = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, toBoolean)));
        Assert.Equal(("cms.type_change_not_allowed", "f1", "image", "boolean"), (type.Code, type.Text("fieldId"), type.Text("from"), type.Text("to")));

        var retarget = CmsApi.Schema(collection);
        CmsApi.Fields(retarget)[1]!["target"] = teams.ToString();
        var target = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, retarget)));
        Assert.Equal(("cms.target_change_not_allowed", "f2"), (target.Code, target.Text("fieldId")));

        var emptyToBoolean = CmsApi.Schema(collection);
        CmsApi.Fields(emptyToBoolean)[2] = CmsApi.Field("f3", "empty", "boolean");
        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, emptyToBoolean));
    }
}
