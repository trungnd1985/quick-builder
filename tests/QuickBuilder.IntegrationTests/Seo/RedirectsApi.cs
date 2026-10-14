using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>Requests to the redirects endpoints (017 contracts/seo-api.yaml).</summary>
internal static class RedirectsApi
{
    public static string Root(Guid siteId) => $"/api/v1/pages/sites/{siteId}/redirects";

    public static object ToPath(string path) => new { kind = "path", path };

    public static object ToUrl(string url) => new { kind = "url", url };

    public static object ToPage(Guid pageId) => new { kind = "page", pageId };

    public static object ToItem(Guid collectionId, Guid itemId) => new { kind = "item", collectionId, itemId };

    public static Task<HttpResponseMessage> CreateAsync(
        HttpClient client, Guid siteId, string source, object target, int statusCode = 301, string match = "exact", bool enabled = true, string? note = null) =>
        client.PostAsJsonAsync(new Uri(Root(siteId), UriKind.Relative), new { source, match, target, statusCode, enabled, note });

    public static async Task<JsonElement> CreateOkAsync(
        HttpClient client, Guid siteId, string source, object target, int statusCode = 301, string match = "exact", bool enabled = true, string? note = null) =>
        await PagesApi.OkAsync(await CreateAsync(client, siteId, source, target, statusCode, match, enabled, note).ConfigureAwait(false), HttpStatusCode.Created).ConfigureAwait(false);

    public static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client, Guid siteId, Guid id, uint version, string source, object target, int statusCode = 301, string match = "exact", bool enabled = true, string? note = null) =>
        client.PutAsJsonAsync(new Uri($"{Root(siteId)}/{id}", UriKind.Relative), new { source, match, target, statusCode, enabled, note, version });

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId, Guid id) =>
        client.GetAsync(new Uri($"{Root(siteId)}/{id}", UriKind.Relative));

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid id, uint version) =>
        client.DeleteAsync(new Uri($"{Root(siteId)}/{id}?version={version}", UriKind.Relative));

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId, string query = "") =>
        client.GetAsync(new Uri($"{Root(siteId)}{query}", UriKind.Relative));

    public static Task<HttpResponseMessage> ImportAsync(HttpClient client, Guid siteId, byte[] file)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        return client.PostAsync(new Uri($"{Root(siteId)}/import", UriKind.Relative), content);
    }

    public static Task<HttpResponseMessage> ImportAsync(HttpClient client, Guid siteId, string csv) =>
        ImportAsync(client, siteId, Encoding.UTF8.GetBytes(csv));

    public static Task<HttpResponseMessage> ExportAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri($"{Root(siteId)}/export", UriKind.Relative));

    /// <summary>A visitor's request; the host client never follows redirects, so the response can be read.</summary>
    public static Task<HttpResponseMessage> VisitAsync(QuickBuilder.IntegrationTests.Infrastructure.QuickBuilderApp app, string host, string address) =>
        app.CreateClientForHost(host).GetAsync(new Uri(address, UriKind.Relative));
}
