using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 3: deleting files — permanent, and Editors only their own (003 FR-004,
/// FR-025, clarifications Q2 and Q4, research M-13, M-18).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeleteFileTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;
    private FaultyStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _scenario = await MediaScenario.CreateAsync(
            database,
            "delete",
            services => services.AddSingleton<IFileStorage>(sp => new FaultyStorage(sp.GetRequiredService<LocalFileStorage>()))).ConfigureAwait(false);
        _storage = (FaultyStorage)_scenario.App.Services.GetRequiredService<IFileStorage>();
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Administrators_and_designers_delete_anyones_file_and_editors_their_own()
    {
        var (editor, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var (designer, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);

        var editorsOwn = await MediaApi.UploadOkAsync(editor, _scenario.SiteA).ConfigureAwait(true);
        var forDesigner = await MediaApi.UploadOkAsync(editor, _scenario.SiteA).ConfigureAwait(true);
        var forAdmin = await MediaApi.UploadOkAsync(designer, _scenario.SiteA).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await MediaApi.DeleteAsync(editor, _scenario.SiteA, editorsOwn.Id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await MediaApi.DeleteAsync(designer, _scenario.SiteA, forDesigner.Id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, forAdmin.Id).ConfigureAwait(true)).StatusCode);

        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
    }

    /// <summary>US3 scenario 5: an Editor cannot delete someone else's file.</summary>
    [Fact]
    public async Task An_editor_deleting_someone_elses_file_is_forbidden_and_the_file_is_untouched()
    {
        var (editor, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var theirs = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Gif, "gif", "image/gif").ConfigureAwait(true);

        var response = await MediaApi.DeleteAsync(editor, _scenario.SiteA, theirs.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("identity.forbidden", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);

        Assert.Single(await _scenario.RowsAsync().ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, theirs.PublicPath).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task The_forbidden_body_matches_identitys_forbidden_body()
    {
        var (editor, _) = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var theirs = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var media = await MediaApi.DeleteAsync(editor, _scenario.SiteA, theirs.Id).ConfigureAwait(true);

        // Identity's own 403 for a capability the Editor lacks on a site they can see.
        var identity = await editor.PostAsync(
            new Uri($"/api/v1/sites/{_scenario.SiteA}/host-names", UriKind.Relative),
            new StringContent("{\"hostName\":\"x.site-a.test\"}", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Forbidden, identity.StatusCode);
        Assert.Equal(
            await SignInHelpers.BodyWithoutTraceIdAsync(identity).ConfigureAwait(true),
            await SignInHelpers.BodyWithoutTraceIdAsync(media).ConfigureAwait(true));
    }

    [Fact]
    public async Task Missing_sites_and_files_answer_not_found()
    {
        var (editorOfB, _) = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Editor)).ConfigureAwait(true);
        var onA = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        var onB = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true);
        var siteNotFound = await SignInHelpers.BodyWithoutTraceIdAsync(
            await _scenario.Admin.GetAsync(new Uri($"/api/v1/sites/{Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

        // A site the caller cannot see, and a made-up site for an Administrator: "no such site".
        var hidden = await MediaApi.DeleteAsync(editorOfB, _scenario.SiteA, onA.Id).ConfigureAwait(true);
        var madeUp = await MediaApi.DeleteAsync(_scenario.Admin, Guid.NewGuid(), onA.Id).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, madeUp.StatusCode);
        Assert.Equal(siteNotFound, await SignInHelpers.BodyWithoutTraceIdAsync(hidden).ConfigureAwait(true));
        Assert.Equal(siteNotFound, await SignInHelpers.BodyWithoutTraceIdAsync(madeUp).ConfigureAwait(true));

        // A file that does not exist, or belongs to another site: "no such file".
        foreach (var fileId in new[] { Guid.NewGuid(), onB.Id })
        {
            var response = await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, fileId).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("media.not_found", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        }

        Assert.Equal(2, (await _scenario.RowsAsync().ConfigureAwait(true)).Count);
    }

    /// <summary>US3 scenario 3, FR-004: gone from the library, the address and storage; permanent.</summary>
    [Fact]
    public async Task A_deleted_file_is_gone_everywhere_and_a_second_delete_is_not_found()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, file.Id).ConfigureAwait(true)).StatusCode);

        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
        Assert.False(File.Exists(_scenario.ObjectPath(_scenario.SiteA, file.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, file.Id).ConfigureAwait(true)).StatusCode);
        Assert.Empty((await MediaApi.ListOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true)).Items);

        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, file.Id).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>M-13: a failing purge never fails the delete; the sweep removes the bytes later.</summary>
    [Fact]
    public async Task A_failing_purge_still_deletes_and_leaves_the_bytes_for_the_sweep()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        _storage.FailDelete = new IOException("Simulated storage failure.");

        var response = await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, file.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
        Assert.True(File.Exists(_scenario.ObjectPath(_scenario.SiteA, file.Id)), "The bytes stay until the sweep.");
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Deleting_without_signing_in_is_refused()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var response = await MediaApi.DeleteAsync(_scenario.App.CreateAnonymousClient(), _scenario.SiteA, file.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(await _scenario.RowsAsync().ConfigureAwait(true));
    }
}
