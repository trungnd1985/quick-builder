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
/// as gone: <c>SubmitEndpointTests</c> covers it. The admin requests arrive with the Submissions
/// endpoints (tasks T089).
/// </remarks>
internal sealed class FormsIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Forms";

    public IReadOnlyList<string> CoveredEndpoints { get; } = [];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(admin);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-contact").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var path = PagesApi.PagePath(siteId, page);
        await PublishingApi.SaveAsync(admin, path, Forms.FormsWorld.Document(Forms.FormsWorld.Form("probe-form"))).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, path).ConfigureAwait(false);

        using var visitor = app.CreateClientForHost(hostName);
        using var content = new FormUrlEncodedContent(
            [new("_page", page.ToString()), new("_path", "/probe-contact"), new("f.e00000000001", "probe@example.com")]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/_forms/probe-form", UriKind.Relative)) { Content = content };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        (await visitor.SendAsync(request).ConfigureAwait(false)).EnsureSuccessStatusCode();

        return new SeededRecords(["probe-form", page.ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other) => [];

    public Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded) => Task.FromResult(true);
}
