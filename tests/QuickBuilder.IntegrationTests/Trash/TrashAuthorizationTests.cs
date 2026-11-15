using System.Net;
using System.Net.Http.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Restore mirrors delete; permanent delete is for Administrators (021 FR-013, FR-021, SC-007,
/// research TR-10). Other sites' entries are covered by <c>TrashIsolationProbe</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrashAuthorizationTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_editor_can_see_a_trashed_page_but_not_restore_it()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-auth-page").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var page = (await PagesApi.SeedTreeAsync(admin, site, "about").ConfigureAwait(true))["/about"];
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, page, 1).ConfigureAwait(true)).ConfigureAwait(true);
        var editor = await scenario.UserAsync((site, SystemRole.Editor)).ConfigureAwait(true);

        var list = await PagesApi.OkAsync(await editor.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        var entry = Assert.Single(list.GetProperty("entries").EnumerateArray());
        Assert.False(entry.GetProperty("canRestore").GetBoolean());
        Assert.False(entry.GetProperty("canPurge").GetBoolean());

        var restore = await editor.PostAsJsonAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{page}/restore", UriKind.Relative), new { confirmedAddress = "/about" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, restore.StatusCode);
    }

    [Fact]
    public async Task An_editor_restores_their_own_file_but_not_someone_elses()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-auth-file").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var editor = await scenario.UserAsync((site, SystemRole.Editor)).ConfigureAwait(true);
        var own = await MediaApi.UploadOkAsync(editor, site).ConfigureAwait(true);
        var others = await MediaApi.UploadOkAsync(admin, site).ConfigureAwait(true);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(editor, site, own.Id).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, others.Id).ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.OkAsync(await RestoreFileAsync(editor, site, own.Id).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, (await RestoreFileAsync(editor, site, others.Id).ConfigureAwait(true)).StatusCode);
    }

    [Theory]
    [InlineData(SystemRole.Designer)]
    [InlineData(SystemRole.Editor)]
    public async Task Only_administrators_delete_permanently_or_empty_the_trash(SystemRole role)
    {
        using var scenario = await PagesScenario.CreateAsync(database, $"trash-auth-{role}".ToLowerInvariant()).ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var page = (await PagesApi.SeedTreeAsync(admin, site, "about").ConfigureAwait(true))["/about"];
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, page, 1).ConfigureAwait(true)).ConfigureAwait(true);
        var member = await scenario.UserAsync((site, role)).ConfigureAwait(true);
        var empty = new { trashedUpTo = DateTimeOffset.UtcNow.AddMinutes(1), expectedPages = 1, expectedComponents = 0, expectedItems = 0, expectedFiles = 0 };

        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{page}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        foreach (var module in new[] { "pages", "cms", "media" })
        {
            var response = await member.PostAsJsonAsync(new Uri($"/api/v1/{module}/sites/{site}/trash/empty", UriKind.Relative), empty).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{page}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    private static Task<HttpResponseMessage> RestoreFileAsync(HttpClient client, Guid site, Guid file) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/media/sites/{site}/trash/files/{file}/restore", UriKind.Relative), new { confirmedAddress = "top" });
}
