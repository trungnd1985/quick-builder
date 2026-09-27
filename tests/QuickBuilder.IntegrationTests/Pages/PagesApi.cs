using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Drives the Pages API through the real HTTP pipeline (010 contracts/pages-api.yaml).
/// </summary>
/// <remarks>
/// Titles and component names it makes up carry a canary marker, so the log scan can prove
/// none of them reaches the logs (FR-037).
/// </remarks>
internal static class PagesApi
{
    public const string CanaryMarker = "zz-canary-";

    public static string PagesPath(Guid siteId) => $"/api/v1/pages/sites/{siteId}/pages";

    public static string PagePath(Guid siteId, Guid pageId) => $"{PagesPath(siteId)}/{pageId}";

    public static string ComponentsPath(Guid siteId) => $"/api/v1/pages/sites/{siteId}/components";

    public static string ComponentPath(Guid siteId, Guid componentId) => $"{ComponentsPath(siteId)}/{componentId}";

    public static string Title(string slug) => $"{CanaryMarker}{slug}";

    // --- Page tree -------------------------------------------------------------------

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid siteId, object body) =>
        client.PostAsJsonAsync(new Uri(PagesPath(siteId), UriKind.Relative), body);

    public static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        Guid siteId,
        string slug,
        Guid? parentId = null,
        bool isHome = false,
        string? title = null) =>
        CreateAsync(client, siteId, new { title = title ?? Title(slug), slug, parentId, isHome });

    public static async Task<JsonElement> CreateOkAsync(
        HttpClient client,
        Guid siteId,
        string slug,
        Guid? parentId = null,
        bool isHome = false,
        string? title = null) =>
        await OkAsync(await CreateAsync(client, siteId, slug, parentId, isHome, title).ConfigureAwait(false), HttpStatusCode.Created)
            .ConfigureAwait(false);

    public static Task<HttpResponseMessage> TreeAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(PagesPath(siteId), UriKind.Relative));

    public static async Task<JsonElement> TreeOkAsync(HttpClient client, Guid siteId) =>
        await OkAsync(await TreeAsync(client, siteId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Every page's address, in the order the tree lists them.</summary>
    public static async Task<string[]> PathsAsync(HttpClient client, Guid siteId) =>
        [.. (await TreeOkAsync(client, siteId).ConfigureAwait(false)).GetProperty("pages").EnumerateArray()
            .Select(p => p.GetProperty("path").GetString()!)];

    /// <summary>One page of the tree, by address.</summary>
    public static async Task<JsonElement> NodeAsync(HttpClient client, Guid siteId, string path) =>
        (await TreeOkAsync(client, siteId).ConfigureAwait(false)).GetProperty("pages").EnumerateArray()
            .Single(p => p.GetProperty("path").GetString() == path);

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId, Guid pageId) =>
        client.GetAsync(new Uri(PagePath(siteId, pageId), UriKind.Relative));

    public static Task<HttpResponseMessage> UpdateAsync(HttpClient client, Guid siteId, Guid pageId, string title, string slug, int version) =>
        client.PutAsJsonAsync(new Uri(PagePath(siteId, pageId), UriKind.Relative), new { title, slug, version });

    public static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid siteId, Guid pageId, Guid? parentId, Guid? afterPageId, int version) =>
        client.PostAsJsonAsync(new Uri($"{PagePath(siteId, pageId)}/move", UriKind.Relative), new { parentId, afterPageId, version });

    public static Task<HttpResponseMessage> MakeHomeAsync(HttpClient client, Guid siteId, Guid pageId, int version) =>
        client.PostAsJsonAsync(new Uri($"{PagePath(siteId, pageId)}/make-home", UriKind.Relative), new { version });

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid pageId, int version) =>
        client.DeleteAsync(new Uri($"{PagePath(siteId, pageId)}?version={version}", UriKind.Relative));

    /// <summary>
    /// Builds a tree from an indented outline, two spaces a level, parents first. A slug
    /// ending in <c>*</c> is the home page. Returns every page's id by address.
    /// </summary>
    public static async Task<Dictionary<string, Guid>> SeedTreeAsync(HttpClient client, Guid siteId, string outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var parents = new List<Guid>();
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var raw in outline.Split('\n').Where(l => l.Trim().Length > 0))
        {
            var line = raw.TrimEnd('\r');
            var depth = (line.Length - line.TrimStart().Length) / 2;
            var slug = line.Trim();
            var isHome = slug.EndsWith('*');
            slug = slug.TrimEnd('*');

            var created = await CreateOkAsync(client, siteId, slug, depth == 0 ? null : parents[depth - 1], isHome).ConfigureAwait(false);
            var id = created.GetProperty("id").GetGuid();
            ids[created.GetProperty("path").GetString()!] = id;

            if (parents.Count > depth)
            {
                parents.RemoveRange(depth, parents.Count - depth);
            }

            parents.Add(id);
        }

        return ids;
    }

    // --- Drafts ----------------------------------------------------------------------

    public static Task<HttpResponseMessage> DraftAsync(HttpClient client, string ownerPath) =>
        client.GetAsync(new Uri($"{ownerPath}/draft", UriKind.Relative));

    public static async Task<JsonElement> DraftOkAsync(HttpClient client, string ownerPath) =>
        await OkAsync(await DraftAsync(client, ownerPath).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Saves a draft, with the document as raw JSON text sent exactly as given.</summary>
    public static Task<HttpResponseMessage> SaveDraftAsync(HttpClient client, string ownerPath, string documentJson, uint version)
    {
        var body = $"{{\"version\":{version},\"document\":{documentJson}}}";
        return client.PutAsync(
            new Uri($"{ownerPath}/draft", UriKind.Relative),
            new StringContent(body, Encoding.UTF8, "application/json"));
    }

    /// <summary>Saves a draft based on the current version.</summary>
    public static async Task<HttpResponseMessage> SaveCurrentDraftAsync(HttpClient client, string ownerPath, string documentJson)
    {
        var draft = await DraftOkAsync(client, ownerPath).ConfigureAwait(false);
        return await SaveDraftAsync(client, ownerPath, documentJson, draft.GetProperty("version").GetUInt32()).ConfigureAwait(false);
    }

    /// <summary>A valid page document referencing the given components.</summary>
    public static string DocumentReferencing(params Guid[] componentIds) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            root = new
            {
                id = "root",
                type = "root",
                props = new { },
                children = componentIds.Select((id, i) => new
                {
                    id = $"ref-{i}",
                    type = "component-ref",
                    props = new { componentId = id },
                    children = Array.Empty<object>(),
                }),
            },
        });

    // --- Components ------------------------------------------------------------------

    public static Task<HttpResponseMessage> CreateComponentAsync(HttpClient client, Guid siteId, string name) =>
        client.PostAsJsonAsync(new Uri(ComponentsPath(siteId), UriKind.Relative), new { name });

    public static async Task<JsonElement> CreateComponentOkAsync(HttpClient client, Guid siteId, string name) =>
        await OkAsync(await CreateComponentAsync(client, siteId, name).ConfigureAwait(false), HttpStatusCode.Created)
            .ConfigureAwait(false);

    public static Task<HttpResponseMessage> ListComponentsAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(ComponentsPath(siteId), UriKind.Relative));

    public static Task<HttpResponseMessage> GetComponentAsync(HttpClient client, Guid siteId, Guid componentId) =>
        client.GetAsync(new Uri(ComponentPath(siteId, componentId), UriKind.Relative));

    public static Task<HttpResponseMessage> RenameComponentAsync(HttpClient client, Guid siteId, Guid componentId, string name, uint version) =>
        client.PutAsJsonAsync(new Uri(ComponentPath(siteId, componentId), UriKind.Relative), new { name, version });

    public static Task<HttpResponseMessage> DeleteComponentAsync(HttpClient client, Guid siteId, Guid componentId, uint version) =>
        client.DeleteAsync(new Uri($"{ComponentPath(siteId, componentId)}?version={version}", UriKind.Relative));

    public static Task<HttpResponseMessage> UsagesAsync(HttpClient client, Guid siteId, Guid componentId) =>
        client.GetAsync(new Uri($"{ComponentPath(siteId, componentId)}/usages", UriKind.Relative));

    // --- Responses -------------------------------------------------------------------

    public static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>The refusal's status and problem body, asserting it has an <c>errorCode</c>.</summary>
    public static async Task<Problem> ProblemAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();
        Assert.True(root.TryGetProperty("errorCode", out var code), $"No errorCode in {(int)response.StatusCode}: {body}");
        return new Problem(response.StatusCode, code.GetString()!, root);
    }

    /// <summary>Asserts a refusal with this status and code.</summary>
    public static async Task<Problem> RefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await ProblemAsync(response).ConfigureAwait(false);
        Assert.True(
            problem.Status == status && problem.Code == code,
            $"Expected {(int)status} {code}, got {(int)problem.Status} {problem.Code}: {problem.Body}");
        return problem;
    }
}

internal sealed record Problem(HttpStatusCode Status, string Code, JsonElement Body)
{
    public string? Text(string name) => Body.TryGetProperty(name, out var value) ? value.ToString() : null;
}
