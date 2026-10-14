using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// Typed helpers over <c>/api/v1/sites</c> (009 contracts/sites-api.yaml).
/// </summary>
/// <remarks>
/// Every call goes through the real HTTP surface, so a test exercises authorization,
/// validation and the error mapping exactly as a browser would. Clients come from
/// <see cref="IdentitySeed"/> and already carry the state-changing request header.
/// </remarks>
internal static class SitesApi
{
    public const string Root = "/api/v1/sites";

    public static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        string name,
        IReadOnlyList<string> hostNames,
        string? primaryHostName = null,
        string? scheme = null,
        string? defaultLanguage = null) =>
        client.PostAsJsonAsync(
            new Uri(Root, UriKind.Relative),
            new { name, hostNames, primaryHostName, scheme, defaultLanguage });

    /// <summary>Creates a site and returns its body, failing the test unless it was created.</summary>
    public static async Task<JsonElement> CreateOkAsync(
        HttpClient client,
        string name,
        IReadOnlyList<string> hostNames,
        string? primaryHostName = null,
        string? scheme = null,
        string? defaultLanguage = null)
    {
        var response = await CreateAsync(client, name, hostNames, primaryHostName, scheme, defaultLanguage).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response).ConfigureAwait(false);
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri($"{Root}/{siteId}", UriKind.Relative));

    public static async Task<JsonElement> GetOkAsync(HttpClient client, Guid siteId)
    {
        var response = await GetAsync(client, siteId).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response).ConfigureAwait(false);
    }

    public static Task<HttpResponseMessage> ListAsync(HttpClient client) =>
        client.GetAsync(new Uri(Root, UriKind.Relative));

    public static Task<HttpResponseMessage> LanguagesAsync(HttpClient client) =>
        client.GetAsync(new Uri($"{Root}/languages", UriKind.Relative));

    public static Task<HttpResponseMessage> PutSettingsAsync(
        HttpClient client,
        Guid siteId,
        string name,
        string scheme,
        string defaultLanguage,
        string primaryHostName,
        uint version) =>
        client.PutAsJsonAsync(
            new Uri($"{Root}/{siteId}/settings", UriKind.Relative),
            new { name, scheme, defaultLanguage, primaryHostName, version });

    /// <summary>
    /// Lets every host name of the site serve it (017 FR-014): new sites redirect other host
    /// names to the primary one, and tests of host name resolution need each name to answer.
    /// </summary>
    public static async Task ServeOnEveryHostNameAsync(HttpClient admin, Guid siteId)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var site = await GetOkAsync(admin, siteId).ConfigureAwait(false);
        var response = await admin.PutAsJsonAsync(
            new Uri($"{Root}/{siteId}/search-visibility", UriKind.Relative),
            new { discourageSearchEngines = false, robotsRules = (string?)null, redirectOtherHostNames = false, version = site.GetProperty("version").GetUInt32() }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public static Task<HttpResponseMessage> AddHostNameAsync(HttpClient client, Guid siteId, string hostName) =>
        client.PostAsJsonAsync(new Uri($"{Root}/{siteId}/host-names", UriKind.Relative), new { hostName });

    public static Task<HttpResponseMessage> RemoveHostNameAsync(HttpClient client, Guid siteId, string hostName) =>
        client.DeleteAsync(new Uri($"{Root}/{siteId}/host-names/{Uri.EscapeDataString(hostName)}", UriKind.Relative));

    public static Task<HttpResponseMessage> DisableAsync(HttpClient client, Guid siteId) =>
        client.PostAsync(new Uri($"{Root}/{siteId}/disable", UriKind.Relative), content: null);

    public static Task<HttpResponseMessage> EnableAsync(HttpClient client, Guid siteId) =>
        client.PostAsync(new Uri($"{Root}/{siteId}/enable", UriKind.Relative), content: null);

    /// <summary>An anonymous public request on a host name.</summary>
    public static Task<HttpResponseMessage> PublicGetAsync(QuickBuilderApp app, string hostName, string path = "/")
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.CreateClientForHost(hostName).GetAsync(new Uri(path, UriKind.Relative));
    }

    public static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    /// <summary>The <c>errorCode</c> of a problem response.</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response).ConfigureAwait(false)).TryGetProperty("errorCode", out var code)
            ? code.GetString()
            : null;

    /// <summary>The raw body, for byte-for-byte comparisons of responses that must not differ.</summary>
    public static async Task<string> RawAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
}
