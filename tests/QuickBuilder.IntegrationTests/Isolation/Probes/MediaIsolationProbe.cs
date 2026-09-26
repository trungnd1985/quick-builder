using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Media files: previewing or deleting another site's file through this site's route must
/// not find it, this site's library must not list it, and its public address must not
/// serve it on this site's host. <c>MediaSiteIsolationTests</c> stays as Media's detailed
/// test; this is its entry in the shared suite, and the worked example for new modules.
/// </summary>
internal sealed class MediaIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Media";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/content",
        "DELETE api/v1/media/sites/{siteId:guid}/files/{fileId:guid}",
    ];

    public string? NeverIssuedPublicPath => MediaApi.PublicPath(Guid.NewGuid(), "png");

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var file = await MediaApi.UploadOkAsync(admin, siteId).ConfigureAwait(false);
        return new SeededRecords([file.Id.ToString()], [file.PublicPath]);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var id in other.Ids)
        {
            yield return new ProbeRequest(HttpMethod.Get, $"{MediaApi.FilesPath(siteId)}/{id}/content", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Delete, $"{MediaApi.FilesPath(siteId)}/{id}", ProbeKind.Addressed);
        }

        yield return new ProbeRequest(HttpMethod.Get, MediaApi.FilesPath(siteId), ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var page = await MediaApi.ListOkAsync(admin, siteId).ConfigureAwait(false);
        var ids = page.Items.Select(f => f.Id.ToString()).ToHashSet();
        return seeded.Ids.All(ids.Contains);
    }
}
