using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.Media.Contracts;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>Requests of 013 contracts/media-api.yaml (folders, search, alternative text, moves), for tests.</summary>
internal static class LibraryApi
{
    public static string FoldersPath(Guid siteId) => $"/api/v1/media/sites/{siteId}/folders";

    public static string FilePath(Guid siteId, Guid fileId) => $"{MediaApi.FilesPath(siteId)}/{fileId}";

    /// <summary>Uploads <paramref name="bytes"/> named <paramref name="fileName"/>, optionally into a folder.</summary>
    public static async Task<MediaFileDto> UploadNamedAsync(
        HttpClient client, Guid siteId, string fileName, byte[]? bytes = null, string contentType = "image/png", Guid? folderId = null)
    {
        using var content = new ByteArrayContent(bytes ?? SampleFiles.Png);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        var path = MediaApi.FilesPath(siteId) + (folderId is { } f ? $"?folderId={f}" : string.Empty);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Add("X-QuickBuilder-File-Name", Uri.EscapeDataString(fileName));

        var response = await client.SendAsync(request).ConfigureAwait(false);
        return await OkAsync<MediaFileDto>(response, HttpStatusCode.Created).ConfigureAwait(false);
    }

    /// <summary>The raw upload response, for refusals.</summary>
    public static async Task<HttpResponseMessage> UploadToFolderAsync(HttpClient client, Guid siteId, Guid folderId)
    {
        using var content = new ByteArrayContent(SampleFiles.Png);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{MediaApi.FilesPath(siteId)}?folderId={folderId}", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Add("X-QuickBuilder-File-Name", "in-folder.png");
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId, string query) =>
        client.GetAsync(new Uri($"{MediaApi.FilesPath(siteId)}{query}", UriKind.Relative));

    public static async Task<MediaFilePageDto> ListOkAsync(HttpClient client, Guid siteId, string query = "") =>
        await OkAsync<MediaFilePageDto>(await ListAsync(client, siteId, query).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Every item of a listing, following cursors.</summary>
    public static async Task<List<MediaFileDto>> ListAllAsync(HttpClient client, Guid siteId, string query = "", int limit = 3)
    {
        var all = new List<MediaFileDto>();
        string? cursor = null;
        var separator = query.Length == 0 ? "?" : "&";

        do
        {
            var page = await ListOkAsync(
                    client,
                    siteId,
                    $"{query}{separator}limit={limit}" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}"))
                .ConfigureAwait(false);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return all;
    }

    public static Task<HttpResponseMessage> GetFileAsync(HttpClient client, Guid siteId, Guid fileId) =>
        client.GetAsync(new Uri(FilePath(siteId, fileId), UriKind.Relative));

    public static Task<HttpResponseMessage> SetAltTextAsync(HttpClient client, Guid siteId, Guid fileId, string? altText) =>
        client.PatchAsJsonAsync(new Uri(FilePath(siteId, fileId), UriKind.Relative), new { altText });

    public static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid siteId, IEnumerable<Guid> fileIds, Guid? folderId) =>
        client.PostAsJsonAsync(new Uri($"{MediaApi.FilesPath(siteId)}/move", UriKind.Relative), new { fileIds, folderId });

    public static Task<HttpResponseMessage> FoldersAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(FoldersPath(siteId), UriKind.Relative));

    public static async Task<MediaFolderTreeDto> FoldersOkAsync(HttpClient client, Guid siteId) =>
        await OkAsync<MediaFolderTreeDto>(await FoldersAsync(client, siteId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> CreateFolderAsync(HttpClient client, Guid siteId, string name, Guid? parentId = null) =>
        client.PostAsJsonAsync(new Uri(FoldersPath(siteId), UriKind.Relative), new { name, parentId });

    public static async Task<MediaFolderDto> CreateFolderOkAsync(HttpClient client, Guid siteId, string name, Guid? parentId = null) =>
        await OkAsync<MediaFolderDto>(await CreateFolderAsync(client, siteId, name, parentId).ConfigureAwait(false), HttpStatusCode.Created)
            .ConfigureAwait(false);

    public static Task<HttpResponseMessage> RenameFolderAsync(HttpClient client, Guid siteId, Guid folderId, string name) =>
        client.PatchAsJsonAsync(new Uri($"{FoldersPath(siteId)}/{folderId}", UriKind.Relative), new { name });

    /// <summary>Moves a folder; a null parent means the top level (the member is sent either way).</summary>
    public static Task<HttpResponseMessage> MoveFolderAsync(HttpClient client, Guid siteId, Guid folderId, Guid? parentId)
    {
        var body = parentId is { } p ? $$"""{"parentId":"{{p}}"}""" : """{"parentId":null}""";
        return client.PatchAsync(
            new Uri($"{FoldersPath(siteId)}/{folderId}", UriKind.Relative),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    }

    public static Task<HttpResponseMessage> DeleteFolderAsync(HttpClient client, Guid siteId, Guid folderId) =>
        client.DeleteAsync(new Uri($"{FoldersPath(siteId)}/{folderId}", UriKind.Relative));

    public static async Task<T> OkAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, MediaApi.Json)!;
    }

    /// <summary>Asserts a problem response with the given status and error code.</summary>
    public static async Task RefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(code, problem.RootElement.GetProperty("errorCode").GetString());
    }
}
