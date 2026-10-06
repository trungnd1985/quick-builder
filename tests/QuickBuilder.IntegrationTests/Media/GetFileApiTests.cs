using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// One file's details (013 contracts/media-api.yaml <c>getMediaFile</c>; Principle X, analysis C2).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetFileApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "media-get-file").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task A_file_comes_with_every_013_field()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(_scenario.Admin, Site, "Logos").ConfigureAwait(true);
        var uploaded = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "logo.png", folderId: folder.Id).ConfigureAwait(true);
        await LibraryApi.SetAltTextAsync(_scenario.Admin, Site, uploaded.Id, "Company logo").ConfigureAwait(true);

        var file = await LibraryApi.OkAsync<MediaFileDto>(await LibraryApi.GetFileAsync(_scenario.Admin, Site, uploaded.Id).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(folder.Id, file.FolderId);
        Assert.Equal("Company logo", file.AltText);
        Assert.Equal(1, file.ContentVersion);
        Assert.Null(file.ReplacedAtUtc);
        Assert.Null(file.ReplacedBy);
        Assert.Equal(MediaUsageSummaryDto.None, file.Usage);
        Assert.False(string.IsNullOrEmpty(file.ThumbnailPath));
        Assert.True(file.Available);
        Assert.True(file.CanDelete);
        Assert.True(file.CanReplace);
    }

    [Fact]
    public async Task Delete_and_replace_rights_follow_ownership()
    {
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor)).ConfigureAwait(true);
        var (otherEditor, _) = await _scenario.UserAsync((Site, SystemRole.Editor)).ConfigureAwait(true);
        var (designer, _) = await _scenario.UserAsync((Site, SystemRole.Designer)).ConfigureAwait(true);
        var own = await LibraryApi.UploadNamedAsync(editor, Site, "mine.png").ConfigureAwait(true);

        async Task<MediaFileDto> As(HttpClient client) =>
            await LibraryApi.OkAsync<MediaFileDto>(await LibraryApi.GetFileAsync(client, Site, own.Id).ConfigureAwait(true)).ConfigureAwait(true);

        var byOwner = await As(editor).ConfigureAwait(true);
        var byOther = await As(otherEditor).ConfigureAwait(true);
        var byDesigner = await As(designer).ConfigureAwait(true);

        Assert.True(byOwner.CanDelete && byOwner.CanReplace);
        Assert.False(byOther.CanDelete || byOther.CanReplace);
        Assert.True(byDesigner.CanDelete && byDesigner.CanReplace);
    }

    [Fact]
    public async Task Another_sites_file_and_an_outsider_get_not_found()
    {
        var elsewhere = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteB, "b.png").ConfigureAwait(true);
        await LibraryApi.RefusedAsync(
            await LibraryApi.GetFileAsync(_scenario.Admin, Site, elsewhere.Id).ConfigureAwait(true), HttpStatusCode.NotFound, "media.not_found").ConfigureAwait(true);

        var mine = await LibraryApi.UploadNamedAsync(_scenario.Admin, Site, "a.png").ConfigureAwait(true);
        var (outsider, _) = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Editor)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, (await LibraryApi.GetFileAsync(outsider, Site, mine.Id).ConfigureAwait(true)).StatusCode);
    }
}
