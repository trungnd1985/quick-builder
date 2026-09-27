using System.Net;
using System.Text;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Sites;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Saving and reading page drafts against the page schema contract (010 US3, FR-015 to
/// FR-021, SC-003).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DraftApiTests(DatabaseFixture database)
{
    private static string Examples => Path.Combine(FindRepositoryRoot(), "docs", "contracts", "page-schema", "examples");

    [Fact]
    public async Task A_valid_document_round_trips_equal_as_json_with_a_new_version()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-draft-rt").ConfigureAwait(true);
        var header = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var footer = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true)).GetProperty("id").GetGuid());

        var before = await PagesApi.DraftOkAsync(scenario.Admin, page).ConfigureAwait(true);
        var document = await File.ReadAllTextAsync(Path.Combine(Examples, "valid", "header-and-footer.json")).ConfigureAwait(true);
        document = document
            .Replace("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f", header.ToString(), StringComparison.Ordinal)
            .Replace("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10", footer.ToString(), StringComparison.Ordinal);

        var saved = await PagesApi.OkAsync(
            await PagesApi.SaveDraftAsync(scenario.Admin, page, document, before.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        var read = await PagesApi.DraftOkAsync(scenario.Admin, page).ConfigureAwait(true);

        using var sent = JsonDocument.Parse(document);
        Assert.True(JsonElement.DeepEquals(sent.RootElement, read.GetProperty("document")));
        Assert.NotEqual(before.GetProperty("version").GetUInt32(), saved.GetProperty("version").GetUInt32());
        Assert.Equal(saved.GetProperty("version").GetUInt32(), read.GetProperty("version").GetUInt32());
        Assert.Equal(1, read.GetProperty("schemaVersion").GetInt32());
    }

    public static TheoryData<string> InvalidExamples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Examples, "invalid"), "*--*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public async Task Every_invalid_example_is_refused_and_the_draft_is_kept(string file)
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-draft-bad").ConfigureAwait(true);
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());
        var before = (await PagesApi.DraftOkAsync(scenario.Admin, page).ConfigureAwait(true)).GetRawText();

        var body = await File.ReadAllTextAsync(Path.Combine(Examples, "invalid", file)).ConfigureAwait(true);
        var response = await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, body).ConfigureAwait(true);

        var expectedRule = file[..file.IndexOf("--", StringComparison.Ordinal)];
        if (expectedRule == "malformed_json")
        {
            // Not even the envelope parses: the request itself is malformed.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        else
        {
            var problem = await PagesApi.ProblemAsync(response).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadRequest, problem.Status);
            Assert.Equal(expectedRule, problem.Text("rule"));
            Assert.NotNull(problem.Text("pointer"));
        }

        Assert.Equal(before, (await PagesApi.DraftOkAsync(scenario.Admin, page).ConfigureAwait(true)).GetRawText());
    }

    [Fact]
    public async Task A_stale_version_an_oversized_body_and_an_unknown_component_are_refused()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-draft-refuse").ConfigureAwait(true);
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());
        var empty = await File.ReadAllTextAsync(Path.Combine(Examples, "valid", "empty.json")).ConfigureAwait(true);

        var first = await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, empty).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.SaveDraftAsync(scenario.Admin, page, empty, first.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PagesApi.SaveDraftAsync(scenario.Admin, page, empty, first.GetProperty("version").GetUInt32()).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);

        var padding = new string(' ', 1_200_000);
        await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, empty + padding).ConfigureAwait(true),
            HttpStatusCode.RequestEntityTooLarge, "pages.document_too_large").ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, PagesApi.DocumentReferencing(Guid.NewGuid())).ConfigureAwait(true),
            HttpStatusCode.BadRequest, "pages.unknown_component").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_document_just_over_the_limit_inside_an_allowed_body_is_too_large()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-draft-1mib").ConfigureAwait(true);
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());

        // 1 MiB + 1 byte of document: under the body cap, over the document limit.
        var document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";
        document = document.Insert(1, new string(' ', 1_048_577 - Encoding.UTF8.GetByteCount(document)));

        await PagesApi.RefusedAsync(
            await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, document).ConfigureAwait(true),
            HttpStatusCode.RequestEntityTooLarge, "pages.document_too_large").ConfigureAwait(true);
    }

    [Fact]
    public async Task Saving_a_draft_publishes_nothing_and_changes_nothing_public()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-draft-private").ConfigureAwait(true);
        var created = await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "about").ConfigureAwait(true);
        var page = PagesApi.PagePath(scenario.SiteA, created.GetProperty("id").GetGuid());
        var eventsBefore = (await scenario.OutboxAsync().ConfigureAwait(true)).Count;
        var publicBefore = await SitesApi.RawAsync(await SitesApi.PublicGetAsync(scenario.App, PagesScenario.HostA, "/about").ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, page, PagesApi.DocumentReferencing()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(eventsBefore, (await scenario.OutboxAsync().ConfigureAwait(true)).Count);
        var publicAfter = await SitesApi.RawAsync(await SitesApi.PublicGetAsync(scenario.App, PagesScenario.HostA, "/about").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(WithoutTraceId(publicBefore), WithoutTraceId(publicAfter));
    }

    [Fact]
    public async Task A_missing_document_is_a_request_error()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-draft-missing").ConfigureAwait(true);
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());

        var response = await scenario.Admin.PutAsync(
            new Uri($"{page}/draft", UriKind.Relative),
            new StringContent("""{"version":1}""", Encoding.UTF8, "application/json")).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "request.invalid").ConfigureAwait(true);
    }

    private static string WithoutTraceId(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", string.Empty);

    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
