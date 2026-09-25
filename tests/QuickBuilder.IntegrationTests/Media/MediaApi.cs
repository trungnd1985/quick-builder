using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Drives the Media API through the real HTTP pipeline (003 contracts/media-api.yaml).
/// </summary>
/// <remarks>
/// Every file name this generates carries a canary marker, <c>zz-canary-…</c>, so the log
/// scan can prove no file name ever reaches the logs (FR-030, SC-012).
/// </remarks>
internal static class MediaApi
{
    public const string CanaryMarker = "zz-canary-";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A file name containing a unique canary marker.</summary>
    public static string CanaryName(string extension) => $"{CanaryMarker}{Guid.NewGuid():N}.{extension}";

    public static string FilesPath(Guid siteId) => $"/api/v1/media/sites/{siteId}/files";

    /// <summary>Creates a site as an administrator, optionally with one host name. Returns its id.</summary>
    public static async Task<Guid> CreateSiteAsync(HttpClient admin, string name, string? hostName = null)
    {
        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/sites", UriKind.Relative),
            new { name, hostNames = hostName is null ? Array.Empty<string>() : [hostName] }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads one file as the raw body (research M-10).</summary>
    public static Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid siteId,
        byte[] bytes,
        string? fileName,
        string? contentType,
        bool chunked = false)
    {
        HttpContent content = chunked ? new StreamContent(new UnseekableStream(bytes)) : new ByteArrayContent(bytes);
        return UploadAsync(client, siteId, content, fileName, contentType, chunked);
    }

    /// <summary>Uploads arbitrary content, for streams that fail or never end.</summary>
    public static Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid siteId,
        HttpContent content,
        string? fileName,
        string? contentType,
        bool chunked = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(content);

        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(FilesPath(siteId), UriKind.Relative))
        {
            Content = content,
        };

        if (fileName is not null)
        {
            request.Headers.Add("X-QuickBuilder-File-Name", Uri.EscapeDataString(fileName));
        }

        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }

        return client.SendAsync(request);
    }

    /// <summary>Uploads and asserts success, returning the created file.</summary>
    public static async Task<MediaFileDto> UploadOkAsync(
        HttpClient client,
        Guid siteId,
        byte[]? bytes = null,
        string extension = "png",
        string? contentType = "image/png")
    {
        var response = await UploadAsync(client, siteId, bytes ?? SampleFiles.Png, CanaryName(extension), contentType)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<MediaFileDto>(body, Json)!;
    }

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId, string? cursor = null, int? limit = null)
    {
        var query = new List<string>();
        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (limit is not null)
        {
            query.Add($"limit={limit}");
        }

        var path = FilesPath(siteId) + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        return client.GetAsync(new Uri(path, UriKind.Relative));
    }

    public static async Task<MediaFilePageDto> ListOkAsync(HttpClient client, Guid siteId, string? cursor = null, int? limit = null)
    {
        var response = await ListAsync(client, siteId, cursor, limit).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<MediaFilePageDto>(body, Json)!;
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid fileId) =>
        client.DeleteAsync(new Uri($"{FilesPath(siteId)}/{fileId}", UriKind.Relative));

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid siteId, Guid fileId, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{FilesPath(siteId)}/{fileId}/content", UriKind.Relative));
        configure?.Invoke(request);
        return client.SendAsync(request);
    }

    /// <summary>A public GET (or other method) on a site's host name, with no session.</summary>
    public static Task<HttpResponseMessage> PublicAsync(
        QuickBuilderApp app,
        string hostName,
        string path,
        HttpMethod? method = null,
        Action<HttpRequestMessage>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var client = app.CreateClientForHost(hostName);
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));
        configure?.Invoke(request);
        return client.SendAsync(request);
    }

    /// <summary>The public path of a file, as the API reports it.</summary>
    public static string PublicPath(Guid fileId, string extension) => $"/_media/{fileId:N}.{extension}";

    /// <summary>A body with no known length, so the client sends it chunked.</summary>
    private sealed class UnseekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
