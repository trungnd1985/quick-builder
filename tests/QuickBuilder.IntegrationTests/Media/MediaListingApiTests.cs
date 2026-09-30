using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 1: browsing a folder, searching the library or a subtree, filtering and
/// sorting, with keyset paging (013 FR-003, FR-005 – FR-007, research MV-03).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaListingApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "media-listing").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Admin => _scenario.Admin;

    private Guid Site => _scenario.SiteA;

    [Fact]
    public async Task Browsing_shows_one_folder_or_the_top_level()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Team").ConfigureAwait(true);
        var inFolder = await LibraryApi.UploadNamedAsync(Admin, Site, "in.png", folderId: folder.Id).ConfigureAwait(true);
        var atTop = await LibraryApi.UploadNamedAsync(Admin, Site, "top.png").ConfigureAwait(true);

        var top = await LibraryApi.ListAllAsync(Admin, Site).ConfigureAwait(true);
        var inside = await LibraryApi.ListAllAsync(Admin, Site, $"?folderId={folder.Id}").ConfigureAwait(true);

        Assert.Equal([atTop.Id], top.Select(f => f.Id));
        Assert.Equal([inFolder.Id], inside.Select(f => f.Id));
        Assert.Equal(folder.Id, inside[0].FolderId);
    }

    [Fact]
    public async Task Search_matches_names_and_alternative_text_ignoring_case_and_accents()
    {
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Docs").ConfigureAwait(true);
        var sub = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Old", folder.Id).ConfigureAwait(true);
        var brochure = await LibraryApi.UploadNamedAsync(Admin, Site, "BROCHURE-été.pdf", SampleFiles.Pdf, "application/pdf", sub.Id).ConfigureAwait(true);
        var photo = await LibraryApi.UploadNamedAsync(Admin, Site, "desk.png").ConfigureAwait(true);
        await LibraryApi.SetAltTextAsync(Admin, Site, photo.Id, "Anna Nguyễn at her desk").ConfigureAwait(true);
        await LibraryApi.UploadNamedAsync(Admin, Site, "50%_off.png").ConfigureAwait(true);

        Assert.Equal([brochure.Id], (await LibraryApi.ListAllAsync(Admin, Site, "?q=ETE").ConfigureAwait(true)).Select(f => f.Id));
        Assert.Equal([brochure.Id], (await LibraryApi.ListAllAsync(Admin, Site, "?q=broch").ConfigureAwait(true)).Select(f => f.Id));
        Assert.Equal([photo.Id], (await LibraryApi.ListAllAsync(Admin, Site, "?q=nguyen").ConfigureAwait(true)).Select(f => f.Id));

        // Literal text: '%' and '_' match themselves, not anything.
        Assert.Single(await LibraryApi.ListAllAsync(Admin, Site, "?q=" + Uri.EscapeDataString("%_")).ConfigureAwait(true));
        Assert.Empty(await LibraryApi.ListAllAsync(Admin, Site, "?q=" + Uri.EscapeDataString("_x")).ConfigureAwait(true));

        // Whole library by default, even with a folder open; the subtree when asked.
        Assert.Single(await LibraryApi.ListAllAsync(Admin, Site, $"?folderId={sub.Id}&q=desk").ConfigureAwait(true));
        Assert.Equal([brochure.Id], (await LibraryApi.ListAllAsync(Admin, Site, $"?folderId={folder.Id}&scope=subtree&q=e").ConfigureAwait(true)).Select(f => f.Id));
    }

    [Fact]
    public async Task Kind_filters_and_every_sort_page_through_every_file_once()
    {
        var names = new[] { "b.png", "A.png", "c.png", "d.png", "e.png" };
        foreach (var (name, i) in names.Select((n, i) => (n, i)))
        {
            await LibraryApi.UploadNamedAsync(Admin, Site, name, SampleFiles.OfSize(SampleFiles.Png, 2_000 + (i * 97))).ConfigureAwait(true);
        }

        await LibraryApi.UploadNamedAsync(Admin, Site, "z.pdf", SampleFiles.Pdf, "application/pdf").ConfigureAwait(true);

        Assert.Equal(5, (await LibraryApi.ListAllAsync(Admin, Site, "?kind=image").ConfigureAwait(true)).Count);
        Assert.Single(await LibraryApi.ListAllAsync(Admin, Site, "?kind=document").ConfigureAwait(true));

        var byName = await LibraryApi.ListAllAsync(Admin, Site, "?sort=name", limit: 2).ConfigureAwait(true);
        Assert.Equal(["A.png", "b.png", "c.png", "d.png", "e.png", "z.pdf"], byName.Select(f => f.FileName));

        var bySizeDesc = await LibraryApi.ListAllAsync(Admin, Site, "?sort=size&direction=desc", limit: 2).ConfigureAwait(true);
        Assert.Equal(bySizeDesc.OrderByDescending(f => f.SizeBytes).ThenByDescending(f => f.Id).Select(f => f.Id), bySizeDesc.Select(f => f.Id));
        Assert.Equal(6, bySizeDesc.Select(f => f.Id).Distinct().Count());

        var newest = await LibraryApi.ListAllAsync(Admin, Site, string.Empty, limit: 4).ConfigureAwait(true);
        Assert.Equal("z.pdf", newest[0].FileName);
        Assert.Equal(6, newest.Count);
    }

    [Fact]
    public async Task Uploads_during_paging_never_shift_or_repeat_a_file()
    {
        for (var i = 0; i < 6; i++)
        {
            await LibraryApi.UploadNamedAsync(Admin, Site, $"f{i}.png").ConfigureAwait(true);
        }

        var first = await LibraryApi.ListOkAsync(Admin, Site, "?limit=3").ConfigureAwait(true);
        await LibraryApi.UploadNamedAsync(Admin, Site, "late.png").ConfigureAwait(true);
        var second = await LibraryApi.ListOkAsync(Admin, Site, $"?limit=3&cursor={Uri.EscapeDataString(first.NextCursor!)}").ConfigureAwait(true);

        Assert.Empty(first.Items.Select(f => f.Id).Intersect(second.Items.Select(f => f.Id)));
        Assert.Equal(6, first.Items.Concat(second.Items).Count());
        Assert.DoesNotContain(second.Items, f => f.FileName == "late.png");
    }

    [Fact]
    public async Task Total_count_is_the_querys_and_usage_is_present()
    {
        await LibraryApi.UploadNamedAsync(Admin, Site, "one.png").ConfigureAwait(true);
        await LibraryApi.UploadNamedAsync(Admin, Site, "two.png").ConfigureAwait(true);

        var page = await LibraryApi.ListOkAsync(Admin, Site, "?q=one").ConfigureAwait(true);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(0, page.Items[0].Usage.Places);
        Assert.Equal(1, page.Items[0].ContentVersion);
    }

    [Fact]
    public async Task Moving_files_keeps_their_public_address()
    {
        var file = await LibraryApi.UploadNamedAsync(Admin, Site, "keep.png").ConfigureAwait(true);
        var folder = await LibraryApi.CreateFolderOkAsync(Admin, Site, "Elsewhere").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await LibraryApi.MoveAsync(Admin, Site, [file.Id], folder.Id).ConfigureAwait(true)).StatusCode);

        var moved = (await LibraryApi.ListAllAsync(Admin, Site, $"?folderId={folder.Id}").ConfigureAwait(true)).Single();
        Assert.Equal(file.PublicPath, moved.PublicPath);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, moved.PublicPath).ConfigureAwait(true)).StatusCode);
    }

    [Theory]
    [InlineData("?sort=colour")]
    [InlineData("?direction=sideways")]
    [InlineData("?kind=video")]
    [InlineData("?scope=everything")]
    [InlineData("?scope=subtree&q=x")]
    public async Task Unknown_query_values_are_refused(string query) =>
        await LibraryApi.RefusedAsync(await LibraryApi.ListAsync(Admin, Site, query).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.invalid_query").ConfigureAwait(true);

    [Fact]
    public async Task A_long_search_and_a_cursor_from_another_sort_are_refused()
    {
        await LibraryApi.RefusedAsync(
            await LibraryApi.ListAsync(Admin, Site, "?q=" + new string('x', 101)).ConfigureAwait(true), HttpStatusCode.BadRequest, "media.invalid_query").ConfigureAwait(true);

        for (var i = 0; i < 3; i++)
        {
            await LibraryApi.UploadNamedAsync(Admin, Site, $"c{i}.png").ConfigureAwait(true);
        }

        var page = await LibraryApi.ListOkAsync(Admin, Site, "?limit=1").ConfigureAwait(true);
        await LibraryApi.RefusedAsync(
            await LibraryApi.ListAsync(Admin, Site, $"?sort=name&cursor={Uri.EscapeDataString(page.NextCursor!)}").ConfigureAwait(true),
            HttpStatusCode.BadRequest,
            "media.invalid_cursor").ConfigureAwait(true);
    }

    [Fact]
    public async Task An_unknown_folder_is_not_found() =>
        await LibraryApi.RefusedAsync(
            await LibraryApi.ListAsync(Admin, Site, $"?folderId={Guid.NewGuid()}").ConfigureAwait(true), HttpStatusCode.NotFound, "media.folder_not_found").ConfigureAwait(true);
}
