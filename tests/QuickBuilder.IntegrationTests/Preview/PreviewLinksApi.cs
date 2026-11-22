using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>Requests of 022 contracts/preview-api.yaml, and opening links, for tests.</summary>
internal static class PreviewLinksApi
{
    public static string LinksPath(Guid siteId) => $"/api/v1/editor/sites/{siteId}/preview-links";

    public static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        Guid siteId,
        string kind,
        Guid documentId,
        DateTimeOffset expiresAtUtc,
        int? versionNumber = null,
        Guid? sampleItemId = null,
        string? label = null) =>
        client.PostAsJsonAsync(
            new Uri(LinksPath(siteId), UriKind.Relative),
            new { documentKind = kind, documentId, versionNumber, sampleItemId, label, expiresAtUtc });

    /// <summary>Creates a link, expecting success; returns its id, full address, and path on the host.</summary>
    public static async Task<CreatedLink> CreateOkAsync(
        HttpClient client, Guid siteId, string kind, Guid documentId, DateTimeOffset expiresAtUtc, int? versionNumber = null, Guid? sampleItemId = null, string? label = null)
    {
        var response = await CreateAsync(client, siteId, kind, documentId, expiresAtUtc, versionNumber, sampleItemId, label).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201, got {(int)response.StatusCode}: {text}");

        using var body = JsonDocument.Parse(text);
        var url = body.RootElement.GetProperty("url").GetString()!;
        return new CreatedLink(body.RootElement.GetProperty("link").GetProperty("id").GetGuid(), url, new Uri(url).AbsolutePath);
    }

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId, string kind, Guid documentId) =>
        client.GetAsync(new Uri($"{LinksPath(siteId)}?documentKind={kind}&documentId={documentId}", UriKind.Relative));

    public static Task<HttpResponseMessage> SiteListAsync(HttpClient client, Guid siteId, string query = "") =>
        client.GetAsync(new Uri($"{LinksPath(siteId)}/all{query}", UriKind.Relative));

    public static Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid siteId, Guid linkId) =>
        client.PostAsync(new Uri($"{LinksPath(siteId)}/{linkId}/revoke", UriKind.Relative), content: null);

    public static Task<HttpResponseMessage> RevokeAllAsync(HttpClient client, Guid siteId, string? kind = null, Guid? documentId = null) =>
        client.PostAsJsonAsync(new Uri($"{LinksPath(siteId)}/revoke-all", UriKind.Relative), new { documentKind = kind, documentId });

    /// <summary>Opens a link's path as a visitor without an account would, on <paramref name="host"/>, from <paramref name="address"/>.</summary>
    public static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> OpenAsync(
        QuickBuilderApp app, string path, string host, string address = "198.51.100.7")
    {
        using var client = app.CreateClientForHost(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add(IdentitySeed.RemoteAddressHeader, address);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false), response);
    }
}

/// <param name="Path">The link's path, to request on any host name.</param>
internal sealed record CreatedLink(Guid Id, string Url, string Path);
