using System.Net;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Editing sessions (023 FR-013, FR-023, Principle V): another site's sessions cannot be kept
/// alive, claimed, moved, taken over or released, and its lock holders are never listed.
/// </summary>
/// <remarks>
/// The seeded session holds the lock of a page of its own site, so a request that wrongly
/// reached it would show: it would move or end the lock, and <see cref="StillIntactAsync"/>
/// checks the lock is still held by it.
/// </remarks>
internal sealed class EditingIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Editing";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "POST api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/heartbeat",
        "POST api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/claim",
        "POST api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/move",
        "DELETE api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-editing").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var session = EditingApi.SessionId(await EditingApi.OpenOkAsync(admin, siteId, "page", page).ConfigureAwait(false));
        await EditingApi.ClaimOkAsync(admin, siteId, session).ConfigureAwait(false);
        return new SeededRecords([session.ToString(), page.ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var session = EditingApi.SessionPath(siteId, Guid.Parse(other.Ids[0]));
        yield return new ProbeRequest(HttpMethod.Post, $"{session}/heartbeat", ProbeKind.Addressed, new { active = true });
        yield return new ProbeRequest(HttpMethod.Post, $"{session}/claim", ProbeKind.Addressed, new { });
        yield return new ProbeRequest(HttpMethod.Post, $"{session}/move", ProbeKind.Addressed, new { holderSessionId = other.Ids[0] });
        yield return new ProbeRequest(HttpMethod.Delete, session, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/editor/sites/{siteId}/editing?kind=page", ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await EditingApi.HeartbeatAsync(admin, siteId, Guid.Parse(seeded.Ids[0])).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var presence = await PagesApi.OkAsync(response).ConfigureAwait(false);
        return EditingApi.Role(presence) == "holder";
    }
}
