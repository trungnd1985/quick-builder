using System.Net;
using System.Text;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 2: importing and exporting redirects (017 FR-019, SC-002; research SE-13).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectImportTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "seo-redirect-import").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Guid Site => _scenario.SiteA;

    private static byte[] Fixture() =>
        File.ReadAllBytes(Path.Combine(FixturesDirectory(), "redirects-300.csv"));

    private static string FixturesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "tests", "fixtures");
    }

    [Fact]
    public async Task Valid_rows_are_added_and_refused_ones_reported_by_line_without_their_content()
    {
        await RedirectsApi.CreateOkAsync(_scenario.Admin, Site, "/existing-one", RedirectsApi.ToPath("/a")).ConfigureAwait(true);
        await RedirectsApi.CreateOkAsync(_scenario.Admin, Site, "/existing-two", RedirectsApi.ToPath("/b")).ConfigureAwait(true);
        await RedirectsApi.CreateOkAsync(_scenario.Admin, Site, "/loop-to", RedirectsApi.ToPath("/loop-from")).ConfigureAwait(true);

        var result = await PagesApi.OkAsync(await RedirectsApi.ImportAsync(_scenario.Admin, Site, Fixture()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(297, result.GetProperty("added").GetInt32());
        var refused = result.GetProperty("refused").EnumerateArray()
            .Select(r => (r.GetProperty("line").GetInt32(), r.GetProperty("errorCode").GetString()))
            .ToList();
        Assert.Equal([(152, "redirects.duplicate"), (300, "redirects.duplicate"), (301, "redirects.loop")], refused);
        Assert.DoesNotContain("existing", result.GetRawText(), StringComparison.Ordinal);

        var list = await PagesApi.OkAsync(await RedirectsApi.ListAsync(_scenario.Admin, Site).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(300, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Rows_are_checked_against_the_rows_before_them()
    {
        var result = await PagesApi.OkAsync(await RedirectsApi.ImportAsync(
            _scenario.Admin, Site, "/same,/a,301\n/same,/b,301\n/x,/y,301\n/y,/x,301\n").ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(2, result.GetProperty("added").GetInt32());
        Assert.Equal(
            ["redirects.duplicate", "redirects.loop"],
            result.GetProperty("refused").EnumerateArray().Select(r => r.GetProperty("errorCode").GetString()));
    }

    [Fact]
    public async Task Malformed_rows_are_refused()
    {
        var result = await PagesApi.OkAsync(await RedirectsApi.ImportAsync(
            _scenario.Admin, Site, "/a,/b\n/c,/d,307\n,/e,301\n").ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(0, result.GetProperty("added").GetInt32());
        Assert.All(result.GetProperty("refused").EnumerateArray(), r => Assert.Equal("redirects.import_row_malformed", r.GetProperty("errorCode").GetString()));
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_refused_whole()
    {
        var latin1 = Encoding.Latin1.GetBytes("/café,/b,301\n/ok,/c,301\n");

        var response = await RedirectsApi.ImportAsync(_scenario.Admin, Site, latin1).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "redirects.import_encoding").ConfigureAwait(true);
        var list = await PagesApi.OkAsync(await RedirectsApi.ListAsync(_scenario.Admin, Site).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_file_over_the_row_limit_is_refused_whole()
    {
        var csv = string.Concat(Enumerable.Range(1, 5001).Select(i => $"/r{i},/t,301\n"));

        var response = await RedirectsApi.ImportAsync(_scenario.Admin, Site, csv).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.RequestEntityTooLarge, "redirects.import_too_large").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_file_over_a_mebibyte_is_refused_whole()
    {
        var response = await RedirectsApi.ImportAsync(_scenario.Admin, Site, new byte[(1024 * 1024) + 1]).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task An_export_re_imports_as_all_duplicates()
    {
        await RedirectsApi.ImportAsync(_scenario.Admin, Site, "/one,/a,301,exact,\"A note, with a comma\"\n/two,https://elsewhere.test/,302\n/three,/b,301,prefix\n").ConfigureAwait(true);

        var export = await RedirectsApi.ExportAsync(_scenario.Admin, Site).ConfigureAwait(true);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = await export.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.StartsWith("source,target,kind,match,note", csv, StringComparison.Ordinal);
        Assert.Contains("\"A note, with a comma\"", csv, StringComparison.Ordinal);

        var again = await PagesApi.OkAsync(await RedirectsApi.ImportAsync(_scenario.Admin, Site, csv).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(3, again.GetProperty("refused").GetArrayLength());
    }

    [Fact]
    public async Task Imported_redirects_are_recorded_as_imported()
    {
        await RedirectsApi.ImportAsync(_scenario.Admin, Site, "/imported,/a,301\n").ConfigureAwait(true);

        var list = await PagesApi.OkAsync(await RedirectsApi.ListAsync(_scenario.Admin, Site).ConfigureAwait(true)).ConfigureAwait(true);
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        var read = await PagesApi.OkAsync(await RedirectsApi.GetAsync(_scenario.Admin, Site, id).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal("import", read.GetProperty("origin").GetString());
        Assert.Equal("imported", read.GetProperty("history")[0].GetProperty("action").GetString());
    }
}
