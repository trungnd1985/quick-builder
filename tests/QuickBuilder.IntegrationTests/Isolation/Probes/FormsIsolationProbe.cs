using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Forms and submissions (018 FR-024, Principle V): another site's submissions cannot be read,
/// changed, exported or deleted, and never appear in this site's lists.
/// </summary>
/// <remarks>
/// It seeds a published form and one submission through the public endpoint, on the site's own
/// host name. A submission for another site's form, sent to this site's host name, is refused
/// as gone: <c>SubmitEndpointTests</c> covers it. Deleting and exporting name their records in
/// the body or the filters, so they are sent as listings that must not touch the other site's
/// rows; the retention impact is a count, sent the same way. Spam and restore arrive with US5.
/// </remarks>
internal sealed class FormsIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Forms";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/forms/sites/{siteId:guid}/submissions/{submissionId:guid}",
        "PUT api/v1/forms/sites/{siteId:guid}/submissions/{submissionId:guid}/read",
        "GET api/v1/forms/sites/{siteId:guid}/forms/{formId}/retention-impact",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(admin);

        // Form ids are node ids, chosen per site; a distinct one lets the listings be searched for it.
        var formId = $"probe-{siteId:N}";
        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-contact").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var path = PagesApi.PagePath(siteId, page);
        await PublishingApi.SaveAsync(admin, path, Forms.FormsWorld.Document(Forms.FormsWorld.Form(formId))).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, path).ConfigureAwait(false);

        using var visitor = app.CreateClientForHost(hostName);
        using var content = new FormUrlEncodedContent(
            [new("_page", page.ToString()), new("_path", "/probe-contact"), new("f.e00000000001", "probe@example.com")]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/_forms/{formId}", UriKind.Relative)) { Content = content };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        (await visitor.SendAsync(request).ConfigureAwait(false)).EnsureSuccessStatusCode();

        var list = await PagesApi.OkAsync(await admin.GetAsync(new Uri(Submissions(siteId), UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);
        var submission = list.GetProperty("items")[0].GetProperty("id").GetString()!;

        return new SeededRecords([formId, page.ToString(), submission], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var one = $"{Submissions(siteId)}/{other.Ids[2]}";
        yield return new ProbeRequest(HttpMethod.Get, one, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, $"{one}/read", ProbeKind.Addressed, new { read = true });
        yield return new ProbeRequest(HttpMethod.Delete, Submissions(siteId), ProbeKind.Listing, new { ids = new[] { other.Ids[2] } });
        yield return new ProbeRequest(HttpMethod.Get, Submissions(siteId), ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{Submissions(siteId)}?formId={other.Ids[0]}", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{Submissions(siteId)}/export", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{Submissions(siteId)}/unread-count", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/forms/sites/{siteId}/forms", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/forms/sites/{siteId}/forms/{other.Ids[0]}/retention-impact?days=30", ProbeKind.Listing);
    }

    /// <summary>The seeded submission is still there and still unread: no other site's request reached it.</summary>
    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(seeded);

        var list = await PagesApi.OkAsync(await admin.GetAsync(new Uri(Submissions(siteId), UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);
        return list.GetProperty("items").EnumerateArray().Any(i =>
            i.GetProperty("id").GetString() == seeded.Ids[2] && i.GetProperty("read").ValueKind == JsonValueKind.False);
    }

    private static string Submissions(Guid siteId) => $"/api/v1/forms/sites/{siteId}/submissions";
}
