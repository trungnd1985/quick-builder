using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>The menu endpoints (024 contracts/menus-api.yaml), and menu documents to send them.</summary>
internal static class MenusApi
{
    public static string MenusPath(Guid siteId) => $"/api/v1/pages/sites/{siteId}/menus";

    public static string MenuPath(Guid siteId, Guid menuId) => $"{MenusPath(siteId)}/{menuId}";

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid siteId, string name, Guid? duplicateOf = null) =>
        client.PostAsJsonAsync(new Uri(MenusPath(siteId), UriKind.Relative), duplicateOf is null ? new { name } : (object)new { name, duplicateOf });

    public static async Task<JsonElement> CreateOkAsync(HttpClient client, Guid siteId, string name, Guid? duplicateOf = null) =>
        await PagesApi.OkAsync(await CreateAsync(client, siteId, name, duplicateOf).ConfigureAwait(false), HttpStatusCode.Created).ConfigureAwait(false);

    /// <summary>Creates a menu and returns its id.</summary>
    public static async Task<Guid> CreateIdAsync(HttpClient client, Guid siteId, string name) =>
        (await CreateOkAsync(client, siteId, name).ConfigureAwait(false)).GetProperty("id").GetGuid();

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId) =>
        client.GetAsync(new Uri(MenusPath(siteId), UriKind.Relative));

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId, Guid menuId) =>
        client.GetAsync(new Uri(MenuPath(siteId, menuId), UriKind.Relative));

    public static async Task<JsonElement> GetOkAsync(HttpClient client, Guid siteId, Guid menuId) =>
        await PagesApi.OkAsync(await GetAsync(client, siteId, menuId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid siteId, Guid menuId, string name, uint version) =>
        client.PutAsJsonAsync(new Uri(MenuPath(siteId, menuId), UriKind.Relative), new { name, version });

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid menuId, uint version) =>
        client.DeleteAsync(new Uri($"{MenuPath(siteId, menuId)}?version={version}", UriKind.Relative));

    public static Task<HttpResponseMessage> SaveDraftAsync(HttpClient client, Guid siteId, Guid menuId, string document, uint version) =>
        client.PutAsync(
            new Uri($"{MenuPath(siteId, menuId)}/draft", UriKind.Relative),
            new StringContent($$"""{ "document": {{document}}, "version": {{version}} }""", System.Text.Encoding.UTF8, "application/json"));

    /// <summary>Saves <paramref name="document"/> over the current draft.</summary>
    public static async Task<HttpResponseMessage> SaveCurrentDraftAsync(HttpClient client, Guid siteId, Guid menuId, string document)
    {
        var menu = await GetOkAsync(client, siteId, menuId).ConfigureAwait(false);
        return await SaveDraftAsync(client, siteId, menuId, document, menu.GetProperty("draftVersion").GetUInt32()).ConfigureAwait(false);
    }

    public static async Task<JsonElement> SaveCurrentDraftOkAsync(HttpClient client, Guid siteId, Guid menuId, string document) =>
        await PagesApi.OkAsync(await SaveCurrentDraftAsync(client, siteId, menuId, document).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid siteId, Guid menuId) =>
        client.GetAsync(new Uri($"{MenuPath(siteId, menuId)}/preview", UriKind.Relative));

    public static Task<HttpResponseMessage> UsagesAsync(HttpClient client, Guid siteId, Guid menuId) =>
        client.GetAsync(new Uri($"{MenuPath(siteId, menuId)}/usages", UriKind.Relative));

    public static Task<HttpResponseMessage> ReferencesAsync(HttpClient client, Guid siteId, string query) =>
        client.GetAsync(new Uri($"{MenusPath(siteId)}/references?{query}", UriKind.Relative));

    // --- Documents --------------------------------------------------------------------

    /// <summary>A menu document holding <paramref name="entries"/>.</summary>
    public static string Document(params JsonObject[] entries) =>
        new JsonObject { ["schemaVersion"] = 1, ["entries"] = new JsonArray([.. entries]) }.ToJsonString();

    public static JsonObject Entry(string id, JsonObject target, string? label = null, bool? newTab = null, bool? hidden = null, params JsonObject[] children)
    {
        var entry = new JsonObject { ["id"] = id };
        if (label is not null)
        {
            entry["label"] = label;
        }

        entry["target"] = target;
        if (newTab is not null)
        {
            entry["newTab"] = newTab;
        }

        if (hidden is not null)
        {
            entry["hidden"] = hidden;
        }

        entry["children"] = new JsonArray([.. children]);
        return entry;
    }

    public static JsonObject PageTarget(Guid pageId, string? anchor = null) =>
        anchor is null
            ? new JsonObject { ["type"] = "page", ["pageId"] = pageId.ToString() }
            : new JsonObject { ["type"] = "page", ["pageId"] = pageId.ToString(), ["anchor"] = anchor };

    public static JsonObject ItemTarget(Guid collectionId, Guid itemId) =>
        new() { ["type"] = "item", ["collectionId"] = collectionId.ToString(), ["itemId"] = itemId.ToString() };

    public static JsonObject UrlTarget(string href) => new() { ["type"] = "url", ["href"] = href };

    public static JsonObject AnchorTarget(string anchor) => new() { ["type"] = "anchor", ["anchor"] = anchor };

    public static JsonObject NoTarget() => new() { ["type"] = "none" };

    /// <summary>A page document with one Section holding a Navigation block for <paramref name="menuId"/>.</summary>
    public static string PageShowing(Guid menuId, string props = "") =>
        $$"""
        { "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [
          { "id": "nav-section", "type": "section", "props": {}, "children": [
            { "id": "nav", "type": "navigation", "props": { "menuId": "{{menuId}}"{{(props.Length == 0 ? string.Empty : ", " + props)}} }, "children": [] } ] } ] } }
        """;
}
