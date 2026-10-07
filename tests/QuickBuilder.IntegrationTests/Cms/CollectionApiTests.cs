using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Defining a collection through the API: create, list, read and save, validation, roles and
/// events (014 US1, FR-001 … FR-019, FR-034).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CollectionApiTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_new_collection_is_empty_at_revision_one_and_its_address_name_is_unique_in_the_site()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-create");

        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "blog-posts", "Blog posts");

        Assert.Equal((1, 0, "blog-posts"), (created.GetProperty("revision").GetInt32(), created.GetProperty("itemCount").GetInt32(), created.GetProperty("slug").GetString()));
        Assert.Equal("""{"schemaVersion":1,"fields":[]}""", created.GetProperty("schema").GetRawText());
        Assert.True(created.GetProperty("canManage").GetBoolean());

        var taken = await PagesApi.ProblemAsync(await CmsApi.CreateAsync(scenario.Admin, scenario.SiteA, "blog-posts"));
        Assert.Equal((HttpStatusCode.Conflict, "cms.slug_taken"), (taken.Status, taken.Code));

        // The same address name on another site is a different collection.
        await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteB, "blog-posts");
    }

    [Theory]
    [InlineData("", "blog", "invalid_name", "/name")]
    [InlineData("Blog", "Blog", "invalid_slug", "/slug")]
    [InlineData("Blog", "1blog", "invalid_slug", "/slug")]
    public async Task Bad_details_are_refused_with_their_rule(string name, string slug, string rule, string location)
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-details");

        var problem = await PagesApi.ProblemAsync(await CmsApi.CreateAsync(scenario.Admin, scenario.SiteA, slug, name));

        Assert.Equal((HttpStatusCode.BadRequest, "cms.invalid_schema", rule, location), (problem.Status, problem.Code, problem.Text("rule"), problem.Text("pointer")));
    }

    [Fact]
    public async Task One_field_of_every_type_is_saved_with_permanent_ids_and_read_back_unchanged()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-every-type");
        var people = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "people");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "blog-posts");
        var id = created.GetProperty("id").GetGuid();

        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(created, CmsApi.EveryType(people.GetProperty("id").GetGuid())));

        var schema = saved.GetProperty("schema");
        Assert.Equal(
            ["f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10"],
            schema.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
        Assert.Equal(["o1", "o2"], schema.GetProperty("fields")[7].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetString()));
        Assert.Equal("f1", schema.GetProperty("titleFieldId").GetString());
        Assert.True(schema.GetProperty("fields")[0].GetProperty("required").GetBoolean());
        Assert.Equal("f8", saved.GetProperty("idMap").GetProperty("~8").GetString());
        Assert.Equal("o2", saved.GetProperty("idMap").GetProperty("~12").GetString());
        Assert.Equal(2, saved.GetProperty("revision").GetInt32());

        var read = await CmsApi.GetOkAsync(scenario.Admin, scenario.SiteA, id);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema.GetRawText()), JsonNode.Parse(read.GetProperty("schema").GetRawText())));

        var list = await PagesApi.OkAsync(await CmsApi.ListAsync(scenario.Admin, scenario.SiteA));
        Assert.Equal(["blog-posts", "people"], list.GetProperty("collections").EnumerateArray().Select(c => c.GetProperty("slug").GetString()).Order());
        Assert.Equal(10, list.GetProperty("collections").EnumerateArray().Single(c => c.GetProperty("slug").GetString() == "blog-posts").GetProperty("fieldCount").GetInt32());
    }

    [Fact]
    public async Task Every_invalid_golden_example_is_refused_with_its_rule_and_pointer()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-golden-api");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "golden");
        var id = created.GetProperty("id").GetGuid();
        var directory = Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts", "collection-schema", "examples", "invalid");
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "expected.json")));

        // Stored-only rules cannot fail for a proposal (contract §5); JSON that does not parse
        // never reaches the validator through the API.
        string[] storedOnly = ["missing_title_field", "temporary_id_in_stored", "malformed_json", "duplicate_property"];

        foreach (var entry in expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
        {
            var rule = entry.Value.GetProperty("rule").GetString()!;
            if (storedOnly.Contains(rule) || entry.Name == "invalid_title_field--not-required.json")
            {
                continue;
            }

            var schema = await File.ReadAllTextAsync(Path.Combine(directory, entry.Name));
            var body = $$"""{"revision":1,"name":"Golden","slug":"golden","schema":{{schema}}}""";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var problem = await PagesApi.ProblemAsync(await scenario.Admin.PutAsync(new Uri(CmsApi.CollectionPath(scenario.SiteA, id), UriKind.Relative), content));

            Assert.Equal(
                (entry.Name, entry.Value.GetProperty("code").GetString(), rule, entry.Value.GetProperty("pointer").GetString()),
                (entry.Name, problem.Code, problem.Text("rule"), problem.Text("pointer")));
        }
    }

    [Fact]
    public async Task Duplicate_properties_are_refused_with_their_pointer()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-duplicate-property");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "dupes");

        const string body = """{"revision":1,"name":"Dupes","slug":"dupes","schema":{"schemaVersion":1,"fields":[{"id":"~1","key":"a","key":"b","name":"A","help":"","required":false,"type":"boolean"}]}}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var problem = await PagesApi.ProblemAsync(await scenario.Admin.PutAsync(new Uri(CmsApi.CollectionPath(scenario.SiteA, created.GetProperty("id").GetGuid()), UriKind.Relative), content));

        Assert.Equal(("duplicate_property", "/fields/0/key"), (problem.Text("rule"), problem.Text("pointer")));
    }

    [Fact]
    public async Task Ids_the_collection_never_issued_and_targets_in_other_sites_are_refused()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-ids");
        var other = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteB, "elsewhere");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var id = created.GetProperty("id").GetGuid();

        var forged = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("f99", "flag", "boolean")) };
        var unknown = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(created, forged)));
        Assert.Equal(("cms.unknown_field_id", "/fields/0/id"), (unknown.Code, unknown.Text("pointer")));

        var foreign = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "author", "reference", settings: new { target = other.GetProperty("id").GetGuid() })),
        };
        var target = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(created, foreign)));
        Assert.Equal((HttpStatusCode.BadRequest, "cms.unknown_target_collection", "/fields/0/target"), (target.Status, target.Code, target.Text("pointer")));
    }

    [Fact]
    public async Task A_save_based_on_an_old_revision_is_refused_with_the_current_one()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-stale");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "flag", "boolean")) };
        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema));

        var stale = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema)));

        Assert.Equal((HttpStatusCode.Conflict, "cms.revision_conflict", "2"), (stale.Status, stale.Code, stale.Text("currentRevision")));
    }

    [Fact]
    public async Task A_save_that_changes_nothing_keeps_the_revision()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-no-change");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");

        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, CmsApi.Schema(created)));

        Assert.Equal(1, saved.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task A_proposal_over_64_kib_is_too_large()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-too-large");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray([.. Enumerable.Range(1, 100).Select(i => (JsonNode)CmsApi.Field($"~{i}", $"field-{i}", "boolean"))]),
        };
        foreach (var field in CmsApi.Fields(schema))
        {
            field!["help"] = new string('x', 300);
            field["name"] = new string('n', 100);
        }

        // 100 fields of the longest names and help texts come to about 48 KiB; a 200-option field takes it over.
        CmsApi.Fields(schema)[0] = CmsApi.Field("~1", "field-1", "option", settings: new
        {
            options = Enumerable.Range(1, 200).Select(i => new { id = $"~{100 + i}", label = $"{i:000} " + new string('o', 96) }),
        });

        var problem = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema)));

        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "cms.proposal_too_large"), (problem.Status, problem.Code));
    }

    [Fact]
    public async Task Editors_can_read_but_not_change_and_strangers_are_told_the_site_does_not_exist()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-roles");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var id = created.GetProperty("id").GetGuid();
        using var editor = await scenario.UserAsync((scenario.SiteA, SystemRole.Editor));
        using var designer = await scenario.UserAsync((scenario.SiteA, SystemRole.Designer));
        using var stranger = await scenario.UserAsync((scenario.SiteB, SystemRole.Designer));

        Assert.False((await CmsApi.GetOkAsync(editor, scenario.SiteA, id)).GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await CmsApi.ListAsync(editor, scenario.SiteA)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CmsApi.CreateAsync(editor, scenario.SiteA, "other")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CmsApi.SaveAsync(editor, scenario.SiteA, id, CmsApi.SaveBody(created, CmsApi.Schema(created)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CmsApi.DeleteAsync(editor, scenario.SiteA, id, 1)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await CmsApi.CreateAsync(designer, scenario.SiteA, "by-designer")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await CmsApi.ListAsync(stranger, scenario.SiteA)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CmsApi.GetAsync(stranger, scenario.SiteA, id)).StatusCode);
    }

    [Fact]
    public async Task Another_sites_collection_is_not_found()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-other-site");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");

        var problem = await PagesApi.ProblemAsync(await CmsApi.GetAsync(scenario.Admin, scenario.SiteB, created.GetProperty("id").GetGuid()));

        Assert.Equal((HttpStatusCode.NotFound, "cms.collection_not_found"), (problem.Status, problem.Code));
    }

    [Fact]
    public async Task Creating_and_saving_publish_events_with_ids_only()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-events");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "flag", "boolean")) };
        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema));

        var events = await scenario.OutboxAsync();

        Assert.Equal(["Cms.CollectionCreated", "Cms.CollectionChanged"], events.Select(e => e.Type));
        Assert.All(events, e => Assert.DoesNotContain(PagesApi.CanaryMarker, e.Payload, StringComparison.Ordinal));
        using var changed = JsonDocument.Parse(events[1].Payload);
        Assert.Equal((2, true, false), (changed.RootElement.GetProperty("Revision").GetInt32(), changed.RootElement.GetProperty("SchemaChanged").GetBoolean(), changed.RootElement.GetProperty("ItemsRewritten").GetBoolean()));
    }
}
