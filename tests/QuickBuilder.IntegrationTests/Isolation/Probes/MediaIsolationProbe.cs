using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Media files and folders: previewing, reading, changing or deleting another site's file or
/// folder through this site's route must not find it, this site's library and folder tree
/// must not list it, and its public address must not serve it on this site's host.
/// <c>MediaSiteIsolationTests</c> stays as Media's detailed test; this is its entry in the
/// shared suite, and the worked example for new modules.
/// </summary>
/// <remarks>
/// Seeds one file and one folder per site. <see cref="SeededRecords.Ids"/> holds the file's id
/// first and the folder's second, so the requests below can tell them apart.
/// </remarks>
internal sealed class MediaIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Media";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/content",
        "DELETE api/v1/media/sites/{siteId:guid}/files/{fileId:guid}",
        "GET api/v1/media/sites/{siteId:guid}/files/{fileId:guid}",
        "PATCH api/v1/media/sites/{siteId:guid}/files/{fileId:guid}",
        "PATCH api/v1/media/sites/{siteId:guid}/folders/{folderId:guid}",
        "DELETE api/v1/media/sites/{siteId:guid}/folders/{folderId:guid}",
        "GET api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/variants/{width:int}",
    ];

    public string? NeverIssuedPublicPath => MediaApi.PublicPath(Guid.NewGuid(), "png");

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var file = await MediaApi.UploadOkAsync(admin, siteId).ConfigureAwait(false);
        var folder = await LibraryApi.CreateFolderOkAsync(admin, siteId, $"Isolated {Guid.NewGuid():N}").ConfigureAwait(false);
        return new SeededRecords([file.Id.ToString(), folder.Id.ToString()], [file.PublicPath]);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var file = other.Ids[0];
        var folder = other.Ids[1];

        yield return new ProbeRequest(HttpMethod.Get, $"{MediaApi.FilesPath(siteId)}/{file}/content", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{MediaApi.FilesPath(siteId)}/{file}", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{MediaApi.FilesPath(siteId)}/{file}/variants/320?v=1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Patch, $"{MediaApi.FilesPath(siteId)}/{file}", ProbeKind.Addressed, new { altText = "probe" });
        yield return new ProbeRequest(HttpMethod.Delete, $"{MediaApi.FilesPath(siteId)}/{file}", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Patch, $"{LibraryApi.FoldersPath(siteId)}/{folder}", ProbeKind.Addressed, new { name = "probe" });
        yield return new ProbeRequest(HttpMethod.Delete, $"{LibraryApi.FoldersPath(siteId)}/{folder}", ProbeKind.Addressed);

        yield return new ProbeRequest(HttpMethod.Get, MediaApi.FilesPath(siteId), ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, LibraryApi.FoldersPath(siteId), ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var page = await MediaApi.ListOkAsync(admin, siteId).ConfigureAwait(false);
        var tree = await LibraryApi.FoldersOkAsync(admin, siteId).ConfigureAwait(false);

        return page.Items.Any(f => f.Id.ToString() == seeded.Ids[0])
            && tree.Folders.Any(f => f.Id.ToString() == seeded.Ids[1]);
    }
}
