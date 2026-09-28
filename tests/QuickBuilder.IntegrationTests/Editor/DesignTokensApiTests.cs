using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Reading a site's design tokens (011 US6, FR-031, FR-032, research ED-14).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DesignTokensApiTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_site_that_never_saved_tokens_reads_the_defaults_at_version_0()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-default").ConfigureAwait(true);

        var tokens = await EditorApi.GetOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        Assert.Equal(0u, tokens.GetProperty("version").GetUInt32());
        Assert.True(tokens.GetProperty("isDefault").GetBoolean());
        Assert.Equal(scenario.SiteA, tokens.GetProperty("siteId").GetGuid());
        Assert.Equal(JsonValueKind.Null, tokens.GetProperty("updatedBy").ValueKind);
        Assert.True(tokens.GetProperty("permissions").GetProperty("canManage").GetBoolean());

        using var expected = JsonDocument.Parse(EditorApi.DefaultDocument);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, tokens.GetProperty("document")));
    }

    [Fact]
    public async Task An_unknown_site_is_not_found()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-nosite").ConfigureAwait(true);

        var problem = await PagesApi.ProblemAsync(await EditorApi.GetAsync(scenario.Admin, Guid.CreateVersion7()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode.NotFound, "sites.not_found"), (problem.Status, problem.Code));
    }

    [Fact]
    public async Task A_member_of_another_site_is_told_the_site_does_not_exist()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-other").ConfigureAwait(true);
        var designerOfB = await scenario.UserAsync((scenario.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        var response = await EditorApi.GetAsync(designerOfB, scenario.SiteA).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_designer_saves_a_set_and_reads_it_back_with_a_new_version()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-save").ConfigureAwait(true);
        var designer = await scenario.UserAsync((scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);
        var set = EditorApi.DefaultWithColor("color-accent", "Accent", "#ff8800");

        var saved = await PagesApi.OkAsync(await EditorApi.SaveAsync(designer, scenario.SiteA, set, 0).ConfigureAwait(true)).ConfigureAwait(true);
        var read = await EditorApi.GetOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        Assert.False(read.GetProperty("isDefault").GetBoolean());
        Assert.NotEqual(0u, read.GetProperty("version").GetUInt32());
        Assert.Equal(saved.GetProperty("version").GetUInt32(), read.GetProperty("version").GetUInt32());
        Assert.Equal("Pages Tester", read.GetProperty("updatedBy").GetProperty("displayName").GetString());
        using var expected = JsonDocument.Parse(set);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, read.GetProperty("document")));

        // Site B is untouched.
        Assert.True((await EditorApi.GetOkAsync(scenario.Admin, scenario.SiteB).ConfigureAwait(true)).GetProperty("isDefault").GetBoolean());

        // And a second save builds on the first.
        var again = await EditorApi.SaveAsync(designer, scenario.SiteA, EditorApi.DefaultDocument, read.GetProperty("version").GetUInt32()).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_set_saved_without_content_widths_reads_back_unchanged_and_valid()
    {
        // 011 spec, Edge Cases: a set from before content widths existed stays valid; the
        // consumers, not the store, fill in the default widths.
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-nowidths").ConfigureAwait(true);
        var node = System.Text.Json.Nodes.JsonNode.Parse(EditorApi.DefaultDocument)!.AsObject();
        node.Remove("contentWidths");
        var set = node.ToJsonString();

        await PagesApi.OkAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, set, 0).ConfigureAwait(true)).ConfigureAwait(true);
        var read = await EditorApi.GetOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        Assert.False(read.GetProperty("document").TryGetProperty("contentWidths", out _));
        var again = await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, read.GetProperty("document").GetRawText(), read.GetProperty("version").GetUInt32()).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_save_based_on_an_old_version_is_out_of_date()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-stale").ConfigureAwait(true);
        var first = await PagesApi.OkAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, EditorApi.DefaultDocument, 0).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, EditorApi.DefaultWithColor("color-x", "X"), first.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        foreach (var stale in new[] { 0u, first.GetProperty("version").GetUInt32() })
        {
            var problem = await PagesApi.ProblemAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, EditorApi.DefaultDocument, stale).ConfigureAwait(true)).ConfigureAwait(true);
            Assert.Equal((HttpStatusCode.Conflict, "editor.out_of_date"), (problem.Status, problem.Code));
        }
    }

    [Fact]
    public async Task A_first_save_that_did_not_see_the_defaults_is_out_of_date()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-first").ConfigureAwait(true);

        var problem = await PagesApi.ProblemAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, EditorApi.DefaultDocument, 7).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode.Conflict, "editor.out_of_date"), (problem.Status, problem.Code));
    }

    [Fact]
    public async Task Two_first_saves_at_once_give_exactly_one_winner()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-race").ConfigureAwait(true);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, EditorApi.DefaultWithColor($"color-r{i}", $"Racer {i}"), 0))).ConfigureAwait(true);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    public static TheoryData<string, string> Refusals()
    {
        var data = new TheoryData<string, string>();
        var examples = Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts", "design-tokens", "examples", "invalid");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(examples, "expected.json")));
        foreach (var entry in expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
        {
            data.Add(entry.Name, entry.Value.GetProperty("code").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_invalid_example_is_refused_with_its_code_and_nothing_is_stored(string file, string code)
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-bad").ConfigureAwait(true);
        var set = await File.ReadAllTextAsync(Path.Combine(
            DraftApiTests.FindRepositoryRoot(), "docs", "contracts", "design-tokens", "examples", "invalid", file)).ConfigureAwait(true);

        var problem = await PagesApi.ProblemAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, set, 0).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode.BadRequest, code), (problem.Status, problem.Code));
        Assert.NotNull(problem.Text("pointer"));
        Assert.True((await EditorApi.GetOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true)).GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task A_set_over_64_kib_is_too_large()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-large").ConfigureAwait(true);
        var padded = EditorApi.DefaultDocument.TrimEnd().TrimEnd('}') + ",\"pad\":\"" + new string('x', 70_000) + "\"}";

        var problem = await PagesApi.ProblemAsync(await EditorApi.SaveAsync(scenario.Admin, scenario.SiteA, padded, 0).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "editor.tokens_too_large"), (problem.Status, problem.Code));
    }

    [Fact]
    public async Task An_editor_reads_the_tokens_but_is_not_offered_changes()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-tokens-editor").ConfigureAwait(true);
        var editor = await scenario.UserAsync((scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);

        var tokens = await EditorApi.GetOkAsync(editor, scenario.SiteA).ConfigureAwait(true);

        Assert.False(tokens.GetProperty("permissions").GetProperty("canManage").GetBoolean());
    }
}
