using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Drives the Cms API through the real HTTP pipeline (014 contracts/cms-api.yaml).
/// </summary>
/// <remarks>
/// Names it makes up carry the canary marker, so the log scan can prove none of them reaches
/// the logs (Principle IX).
/// </remarks>
internal static class CmsApi
{
    public static string CollectionsPath(Guid siteId) => $"/api/v1/cms/sites/{siteId}/collections";

    public static string CollectionPath(Guid siteId, Guid collectionId) => $"{CollectionsPath(siteId)}/{collectionId}";

    public static string PreviewPath(Guid siteId, Guid collectionId) => $"{CollectionPath(siteId, collectionId)}/preview";

    public static string HistoryPath(Guid siteId, Guid collectionId) => $"{CollectionPath(siteId, collectionId)}/history";

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid siteId, string slug, string? name = null, string? description = null) =>
        client.PostAsJsonAsync(new Uri(CollectionsPath(siteId), UriKind.Relative), new { name = name ?? $"{PagesApi.CanaryMarker}{slug}", slug, description });

    public static async Task<JsonElement> CreateOkAsync(HttpClient client, Guid siteId, string slug, string? name = null) =>
        await PagesApi.OkAsync(await CreateAsync(client, siteId, slug, name).ConfigureAwait(false), HttpStatusCode.Created).ConfigureAwait(false);

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(CollectionsPath(siteId), UriKind.Relative));

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId, Guid collectionId) =>
        client.GetAsync(new Uri(CollectionPath(siteId, collectionId), UriKind.Relative));

    public static async Task<JsonElement> GetOkAsync(HttpClient client, Guid siteId, Guid collectionId) =>
        await PagesApi.OkAsync(await GetAsync(client, siteId, collectionId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>A save body: the collection's current details, with the given schema, fill-ins and confirmation.</summary>
    public static object SaveBody(JsonElement collection, JsonNode schema, object? fillIns = null, string? digest = null, string? name = null, string? slug = null) => new
    {
        revision = collection.GetProperty("revision").GetInt32(),
        name = name ?? collection.GetProperty("name").GetString(),
        slug = slug ?? collection.GetProperty("slug").GetString(),
        description = collection.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
        schema,
        fillIns,
        confirmation = digest is null ? null : new { digest },
    };

    public static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid siteId, Guid collectionId, object body) =>
        client.PutAsJsonAsync(new Uri(CollectionPath(siteId, collectionId), UriKind.Relative), body);

    public static async Task<JsonElement> SaveOkAsync(HttpClient client, Guid siteId, Guid collectionId, object body) =>
        await PagesApi.OkAsync(await SaveAsync(client, siteId, collectionId, body).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid siteId, Guid collectionId, object body) =>
        client.PostAsJsonAsync(new Uri(PreviewPath(siteId, collectionId), UriKind.Relative), body);

    public static async Task<JsonElement> PreviewOkAsync(HttpClient client, Guid siteId, Guid collectionId, object body) =>
        await PagesApi.OkAsync(await PreviewAsync(client, siteId, collectionId, body).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Previews, then saves with the preview's digest: what the builder does after the member confirms.</summary>
    public static async Task<(JsonElement Preview, JsonElement Saved)> ConfirmAndSaveAsync(
        HttpClient client, Guid siteId, JsonElement collection, JsonNode schema, object? fillIns = null)
    {
        var id = collection.GetProperty("id").GetGuid();
        var preview = await PreviewOkAsync(client, siteId, id, SaveBody(collection, schema, fillIns)).ConfigureAwait(false);
        var saved = await SaveOkAsync(client, siteId, id, SaveBody(collection, schema, fillIns, preview.GetProperty("digest").GetString())).ConfigureAwait(false);
        return (preview, saved);
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid collectionId, int revision) =>
        client.DeleteAsync(new Uri($"{CollectionPath(siteId, collectionId)}?revision={revision}", UriKind.Relative));

    public static Task<HttpResponseMessage> HistoryAsync(HttpClient client, Guid siteId, Guid collectionId, string query = "") =>
        client.GetAsync(new Uri(HistoryPath(siteId, collectionId) + query, UriKind.Relative));

    /// <summary>The collection's schema as a mutable document, to edit and send back.</summary>
    public static JsonObject Schema(JsonElement collection) => JsonNode.Parse(collection.GetProperty("schema").GetRawText())!.AsObject();

    public static JsonArray Fields(JsonObject schema) => schema["fields"]!.AsArray();

    public static JsonObject Field(string id, string key, string type, bool required = false, object? settings = null)
    {
        var field = new JsonObject { ["id"] = id, ["key"] = key, ["name"] = key, ["help"] = string.Empty, ["required"] = required, ["type"] = type };
        foreach (var (name, value) in JsonSerializer.SerializeToNode(settings ?? new { })!.AsObject().ToList())
        {
            field[name] = value?.DeepClone();
        }

        return field;
    }

    /// <summary>One field of each type, with temporary ids; references target <paramref name="target"/>.</summary>
    public static JsonObject EveryType(Guid target) => new()
    {
        ["schemaVersion"] = 1,
        ["fields"] = new JsonArray(
            Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
            Field("~2", "body", "richText"),
            Field("~3", "reading-time", "number", settings: new { decimals = 0, min = 0 }),
            Field("~4", "featured", "boolean"),
            Field("~5", "published-on", "date", settings: new { withTime = false }),
            Field("~6", "cover", "image"),
            Field("~7", "brochure", "file", settings: new { accept = "any" }),
            Field("~8", "category", "option", settings: new { options = new[] { new { id = "~11", label = "News" }, new { id = "~12", label = "Guides" } } }),
            Field("~9", "author", "reference", settings: new { target }),
            Field("~10", "related", "multiReference", settings: new { target })),
    };
}
