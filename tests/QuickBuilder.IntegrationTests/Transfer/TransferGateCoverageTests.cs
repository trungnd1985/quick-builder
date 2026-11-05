using System.Net;
using Npgsql;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Seo;
using QuickBuilder.Shared.Infrastructure.Data;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// While an import holds a site, every module's content writes to it answer 423
/// <c>site.import_in_progress</c> and its reads keep working (020 FR-028, research ST-07).
/// </summary>
/// <remarks>
/// The gate is a SaveChanges interceptor on each content module's context, so one real write per
/// module and kind of document proves it is in place there; another site's writes go on.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class TransferGateCoverageTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_module_s_writes_are_refused_while_an_import_holds_the_site_and_reads_still_answer()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "gate-coverage").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var page = (await PagesApi.CreateOkAsync(admin, site, "before").ConfigureAwait(true)).GetProperty("id").GetGuid();

        await using var import = new NpgsqlConnection(scenario.App.ConnectionString);
        await import.OpenAsync().ConfigureAwait(true);
        await using var transaction = await import.BeginTransactionAsync().ConfigureAwait(true);
        await SiteWriteLock.EnterExclusiveAsync(import, transaction, site, CancellationToken.None).ConfigureAwait(true);

        var writes = new (string What, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("page", () => PagesApi.CreateAsync(admin, site, "during")),
            ("page draft", () => PagesApi.SaveCurrentDraftAsync(admin, PagesApi.PagePath(site, page), PagesApi.DocumentWithText("during"))),
            ("component", () => PagesApi.CreateComponentAsync(admin, site, "During")),
            ("menu", () => MenusApi.CreateAsync(admin, site, "During")),
            ("redirect", () => RedirectsApi.CreateAsync(admin, site, "/during", RedirectsApi.ToPage(page))),
            ("media file", () => MediaApi.UploadAsync(admin, site, SampleFiles.Png, "during.png", "image/png")),
            ("design tokens", () => EditorApi.SaveCurrentAsync(admin, site, EditorApi.DefaultWithColor("during", "During"))),
            ("collection", () => CmsApi.CreateAsync(admin, site, "during")),
        };

        foreach (var (what, send) in writes)
        {
            var response = await send().ConfigureAwait(true);
            Assert.True(response.StatusCode == (HttpStatusCode)423, $"{what}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync().ConfigureAwait(true)}");
            Assert.Equal("site.import_in_progress", (await PagesApi.ProblemAsync(response).ConfigureAwait(true)).Code);
        }

        // Reads answer, and the other site's writes go on.
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.TreeAsync(admin, site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri($"/api/v1/sites/{site}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PagesApi.CreateAsync(admin, scenario.SiteB, "elsewhere").ConfigureAwait(true)).StatusCode);

        // Nothing was written, and once the import ends, writes go through.
        Assert.DoesNotContain("/during", await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
        await transaction.RollbackAsync().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, (await PagesApi.CreateAsync(admin, site, "after").ConfigureAwait(true)).StatusCode);
    }
}
