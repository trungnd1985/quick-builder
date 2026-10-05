using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 1: folders — create, nest, rename, move, delete when empty — with the rules
/// enforced by the server (013 FR-001 – FR-004, data-model.md §3.2, research MV-02).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaFolderApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "media-folders").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Admin => _scenario.Admin;

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task Folders_nest_rename_move_and_delete_when_empty()
    {
        var team = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Team photos").ConfigureAwait(true);
        var year = await LibraryApi.CreateFolderOkAsync(Admin, Site, "2026", team.Id).ConfigureAwait(true);
        Assert.Equal(team.Id, year.ParentId);

        var renamed = await LibraryApi.OkAsync<QuickBuilder.Media.Contracts.MediaFolderDto>(
            await LibraryApi.RenameFolderAsync(Admin, Site, year.Id, "2026 Q4").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("2026 Q4", renamed.Name);

        var moved = await LibraryApi.OkAsync<QuickBuilder.Media.Contracts.MediaFolderDto>(
            await LibraryApi.MoveFolderAsync(Admin, Site, year.Id, null).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Null(moved.ParentId);

        var tree = await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true);
        Assert.Equal(["2026 Q4", "Team photos"], tree.Folders.Select(f => f.Name));
        Assert.True(tree.Revision >= 4);

        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.DeleteFolderAsync(Admin, Site, year.Id).ConfigureAwait(true)).StatusCode);
        Assert.Single((await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true)).Folders);
    }

    [Fact]
    public async Task A_folder_with_a_file_or_a_folder_in_it_is_not_deleted()
    {
        var withFile = await LibraryApi.CreateFolderOkAsync(Admin, Site, "With file").ConfigureAwait(true);
        await LibraryApi.UploadNamedAsync(Admin, Site, "a.png", folderId: withFile.Id).ConfigureAwait(true);

        var withChild = await LibraryApi.CreateFolderOkAsync(Admin, Site, "With child").ConfigureAwait(true);
        await LibraryApi.CreateFolderOkAsync(Admin, Site, "Child", withChild.Id).ConfigureAwait(true);

        await LibraryApi.RefusedAsync(await LibraryApi.DeleteFolderAsync(Admin, Site, withFile.Id).ConfigureAwait(true), HttpStatusCode.Conflict, "media.folder_not_empty").ConfigureAwait(true);
        await LibraryApi.RefusedAsync(await LibraryApi.DeleteFolderAsync(Admin, Site, withChild.Id).ConfigureAwait(true), HttpStatusCode.Conflict, "media.folder_not_empty").ConfigureAwait(true);

        var tree = await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true);
        Assert.Equal(1, tree.Folders.Single(f => f.Id == withFile.Id).FileCount);
    }

    [Fact]
    public async Task A_file_moved_in_while_the_folder_is_deleted_keeps_the_folder()
    {
        // The handler's own check passes (empty), then the file arrives: the RESTRICT key decides.
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Racing").ConfigureAwait(true);
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "r.png").ConfigureAwait(true);

        var results = await Task.WhenAll(
            LibraryApi.MoveAsync(Admin, Site, [file.Id], folder.Id),
            LibraryApi.DeleteFolderAsync(Admin, Site, folder.Id)).ConfigureAwait(true);

        var tree = await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true);
        var listed = await LibraryApi.GetFileAsync(Admin, Site, file.Id).ConfigureAwait(true);
        var dto = await LibraryApi.OkAsync<QuickBuilder.Media.Contracts.MediaFileDto>(listed).ConfigureAwait(true);

        // Either the delete won (and the move found no folder), or the move won (and the delete
        // was refused). Never a file in a folder that no longer exists.
        if (tree.Folders.Any(f => f.Id == folder.Id))
        {
            Assert.Equal(folder.Id, dto.FolderId);
        }
        else
        {
            Assert.Null(dto.FolderId);
            Assert.Equal(HttpStatusCode.NotFound, results[0].StatusCode);
        }
    }

    [Theory]
    [InlineData("Photos", "photos")]
    [InlineData("Été", "ETE")]
    public async Task Sibling_names_clash_ignoring_case_and_accents(string first, string second)
    {
        await LibraryApi.CreateFolderOkAsync(Admin, Site, first).ConfigureAwait(true);

        await LibraryApi.RefusedAsync(
            await LibraryApi.CreateFolderAsync(Admin, Site, second).ConfigureAwait(true), HttpStatusCode.Conflict, "media.folder_name_taken").ConfigureAwait(true);
    }

    [Fact]
    public async Task Depth_cycles_and_bad_names_are_refused()
    {
        Guid? parent = null;
        for (var level = 1; level <= 8; level++)
        {
            parent = (await LibraryApi.CreateFolderOkAsync(Admin, Site, $"L{level}", parent).ConfigureAwait(true)).Id;
        }

        await LibraryApi.RefusedAsync(await LibraryApi.CreateFolderAsync(Admin, Site, "L9", parent).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.folder_too_deep").ConfigureAwait(true);

        var tree = await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true);
        var top = tree.Folders.Single(f => f.Name == "L1");
        await LibraryApi.RefusedAsync(await LibraryApi.MoveFolderAsync(Admin, Site, top.Id, parent).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.folder_cycle").ConfigureAwait(true);

        await LibraryApi.RefusedAsync(await LibraryApi.CreateFolderAsync(Admin, Site, "   ").ConfigureAwait(true), HttpStatusCode.BadRequest, "media.invalid_folder_name").ConfigureAwait(true);
        await LibraryApi.RefusedAsync(await LibraryApi.CreateFolderAsync(Admin, Site, new string('x', 101)).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.invalid_folder_name").ConfigureAwait(true);
        await LibraryApi.RefusedAsync(await LibraryApi.CreateFolderAsync(Admin, Site, "x", Guid.NewGuid()).ConfigureAwait(true), HttpStatusCode.NotFound, "media.folder_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Two_moves_that_together_would_form_a_cycle_do_not_both_commit()
    {
        var a = await LibraryApi.CreateFolderOkAsync(Admin, Site, "A").ConfigureAwait(true);
        var b = await LibraryApi.CreateFolderOkAsync(Admin, Site, "B").ConfigureAwait(true);

        await Task.WhenAll(
            LibraryApi.MoveFolderAsync(Admin, Site, a.Id, b.Id),
            LibraryApi.MoveFolderAsync(Admin, Site, b.Id, a.Id)).ConfigureAwait(true);

        var tree = await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true);
        var parents = tree.Folders.ToDictionary(f => f.Id, f => f.ParentId);
        Assert.False(parents[a.Id] == b.Id && parents[b.Id] == a.Id, "Both moves committed and formed a cycle.");
    }

    /// <summary>
    /// The same race, interleaved by hand so it never depends on timing: both changes are
    /// validated against the same folders, the first commits, and the second must not.
    /// </summary>
    [Fact]
    public async Task A_change_validated_before_another_committed_is_refused_as_a_conflict()
    {
        var a = await LibraryApi.CreateFolderOkAsync(Admin, Site, "A").ConfigureAwait(true);
        var b = await LibraryApi.CreateFolderOkAsync(Admin, Site, "B").ConfigureAwait(true);

        await using var first = _scenario.App.Services.CreateAsyncScope();
        await using var second = _scenario.App.Services.CreateAsyncScope();
        var firstWrites = first.ServiceProvider.GetRequiredService<FolderWrites>();
        var secondWrites = second.ServiceProvider.GetRequiredService<FolderWrites>();

        var firstView = await firstWrites.LoadAsync(Site, CancellationToken.None).ConfigureAwait(true);
        var secondView = await secondWrites.LoadAsync(Site, CancellationToken.None).ConfigureAwait(true);
        Assert.True(firstView.Move(firstView.Find(a.Id)!, b.Id).IsSuccess);
        Assert.True(secondView.Move(secondView.Find(b.Id)!, a.Id).IsSuccess);

        Assert.True((await firstWrites.SaveAsync(Site, CancellationToken.None).ConfigureAwait(true)).IsSuccess);
        var refused = await secondWrites.SaveAsync(Site, CancellationToken.None).ConfigureAwait(true);

        Assert.True(refused.IsFailure);
        Assert.Equal("media.folder_conflict", refused.Error.Code);
        var parents = (await LibraryApi.FoldersOkAsync(Admin, Site).ConfigureAwait(true)).Folders.ToDictionary(f => f.Id, f => f.ParentId);
        Assert.Equal((b.Id, (Guid?)null), (parents[a.Id]!.Value, parents[b.Id]));
    }

    [Fact]
    public async Task An_editor_may_change_a_folder_a_designer_created()
    {
        var (designer, _) = await _scenario.UserAsync((Site, SystemRole.Designer)).ConfigureAwait(true);
        var (editor, _) = await _scenario.UserAsync((Site, SystemRole.Editor)).ConfigureAwait(true);

        var folder = await LibraryApi.CreateFolderOkAsync(designer, Site, "Designer's").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await LibraryApi.RenameFolderAsync(editor, Site, folder.Id, "Renamed by editor").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.DeleteFolderAsync(editor, Site, folder.Id).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_site_cannot_see_or_change_this_sites_folders()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Private").ConfigureAwait(true);
        var (outsider, _) = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await LibraryApi.FoldersAsync(outsider, Site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await LibraryApi.CreateFolderAsync(outsider, Site, "x").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await LibraryApi.DeleteFolderAsync(outsider, Site, folder.Id).ConfigureAwait(true)).StatusCode);

        // Nor through its own site's route.
        await LibraryApi.RefusedAsync(
            await LibraryApi.DeleteFolderAsync(Admin, _scenario.SiteB, folder.Id).ConfigureAwait(true), HttpStatusCode.NotFound, "media.folder_not_found").ConfigureAwait(true);
    }
}
