using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Drives the Editor API through the real HTTP pipeline (011 contracts/editor-api.yaml).
/// </summary>
internal static class EditorApi
{
    public static string TokensPath(Guid siteId) => $"/api/v1/editor/sites/{siteId}/design-tokens";

    public static string TokenUsagePath(Guid siteId, string tokenId) => $"/api/v1/pages/sites/{siteId}/token-usages/{tokenId}";

    /// <summary>The default token set, as the contract's golden file holds it.</summary>
    public static string DefaultDocument =>
        File.ReadAllText(Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts", "design-tokens", "examples", "valid", "default.json"));

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(TokensPath(siteId), UriKind.Relative));

    public static async Task<JsonElement> GetOkAsync(HttpClient client, Guid siteId) =>
        await PagesApi.OkAsync(await GetAsync(client, siteId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Saves <paramref name="documentJson"/> as the site's token set against <paramref name="version"/>.</summary>
    public static async Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid siteId, string documentJson, uint version)
    {
        ArgumentNullException.ThrowIfNull(client);

        var body = $$"""{"version":{{version}},"document":{{documentJson}}}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PutAsync(new Uri(TokensPath(siteId), UriKind.Relative), content).ConfigureAwait(false);
    }

    /// <summary>Saves against whatever version is current.</summary>
    public static async Task<HttpResponseMessage> SaveCurrentAsync(HttpClient client, Guid siteId, string documentJson)
    {
        var current = await GetOkAsync(client, siteId).ConfigureAwait(false);
        return await SaveAsync(client, siteId, documentJson, current.GetProperty("version").GetUInt32()).ConfigureAwait(false);
    }

    /// <summary>The default set with one extra colour, so a site's set is recognisably its own.</summary>
    public static string DefaultWithColor(string id, string name, string value = "#123456")
    {
        using var document = JsonDocument.Parse(DefaultDocument);
        var root = System.Text.Json.Nodes.JsonNode.Parse(document.RootElement.GetRawText())!;
        root["colors"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["name"] = name, ["value"] = value });
        return root.ToJsonString();
    }

    public static Task<HttpResponseMessage> TokenUsageAsync(HttpClient client, Guid siteId, string tokenId) =>
        client.GetAsync(new Uri(TokenUsagePath(siteId, tokenId), UriKind.Relative));

    public static async Task<JsonElement> TokenUsageOkAsync(HttpClient client, Guid siteId, string tokenId) =>
        await PagesApi.OkAsync(await TokenUsageAsync(client, siteId, tokenId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string path, object body) =>
        client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);
}
