using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>User Story 1: filing files in folders (013 FR-002, FR-003).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MoveFilesTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "media-move").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Admin => _scenario.Admin;

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task Up_to_100_files_move_at_once_and_101_are_refused()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Target").ConfigureAwait(true);
        var files = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            files.Add((await LibraryApi.UploadNamedAsync(Admin, Site, $"m{i}.png").ConfigureAwait(true)).Id);
        }

        // Padded with ids of no file: they change nothing and are not reported.
        var hundred = files.Concat(Enumerable.Range(0, 97).Select(_ => Guid.NewGuid())).ToList();
        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.MoveAsync(Admin, Site, hundred, folder.Id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(3, (await LibraryApi.ListAllAsync(Admin, Site, $"?folderId={folder.Id}").ConfigureAwait(true)).Count);

        await LibraryApi.RefusedAsync(
            await LibraryApi.MoveAsync(Admin, Site, [.. hundred, Guid.NewGuid()], folder.Id).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.too_many_files").ConfigureAwait(true);
    }

    [Fact]
    public async Task Null_moves_to_the_top_level_and_an_unknown_folder_is_refused()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Box").ConfigureAwait(true);
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "x.png", folderId: folder.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.MoveAsync(Admin, Site, [file.Id], null).ConfigureAwait(true)).StatusCode);
        Assert.Contains(await LibraryApi.ListAllAsync(Admin, Site).ConfigureAwait(true), f => f.Id == file.Id);

        await LibraryApi.RefusedAsync(
            await LibraryApi.MoveAsync(Admin, Site, [file.Id], Guid.NewGuid()).ConfigureAwait(true), HttpStatusCode.NotFound, "media.folder_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Another_sites_files_are_ignored_and_its_folders_do_not_exist_here()
    {
        var foreignFile = await LibraryApi.UploadNamedAsync(Admin, _scenario.SiteB, "b.png").ConfigureAwait(true);
        var foreignFolder = await LibraryApi.CreateFolderOkAsync(Admin, _scenario.SiteB, "B's").ConfigureAwait(true);
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "A's").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.MoveAsync(Admin, Site, [foreignFile.Id], folder.Id).ConfigureAwait(true)).StatusCode);
        Assert.Null((await LibraryApi.OkAsync<QuickBuilder.Media.Contracts.MediaFileDto>(
            await LibraryApi.GetFileAsync(Admin, _scenario.SiteB, foreignFile.Id).ConfigureAwait(true)).ConfigureAwait(true)).FolderId);

        var mine = await LibraryApi.UploadNamedAsync(Admin, Site, "a.png").ConfigureAwait(true);
        await LibraryApi.RefusedAsync(
            await LibraryApi.MoveAsync(Admin, Site, [mine.Id], foreignFolder.Id).ConfigureAwait(true), HttpStatusCode.NotFound, "media.folder_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Uploading_into_an_unknown_folder_stores_nothing()
    {
        var before = _scenario.StoredObjects().Count;

        await LibraryApi.RefusedAsync(
            await LibraryApi.UploadToFolderAsync(Admin, Site, Guid.NewGuid()).ConfigureAwait(true), HttpStatusCode.NotFound, "media.folder_not_found").ConfigureAwait(true);

        Assert.Equal(before, _scenario.StoredObjects().Count);
    }
}
