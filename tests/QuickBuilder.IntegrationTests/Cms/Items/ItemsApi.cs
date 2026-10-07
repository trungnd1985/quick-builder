using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// Drives the item API through the real HTTP pipeline (015 contracts/items-api.yaml).
/// </summary>
/// <remarks>Text values carry the canary marker, so the log scans can prove none reaches the logs.</remarks>
internal static class ItemsApi
{
    public static string ItemsPath(Guid siteId, Guid collectionId) => $"{CmsApi.CollectionPath(siteId, collectionId)}/items";

    public static string ItemPath(Guid siteId, Guid collectionId, Guid itemId) => $"{ItemsPath(siteId, collectionId)}/{itemId}";

    /// <summary>A draft body: slug, values keyed by field id, and the schema revision the form was built from.</summary>
    public static object Draft(string slug, JsonObject values, int schemaRevision, int? revision = null) => revision is null
        ? new { slug, values = new JsonObject { ["schemaVersion"] = 1, ["values"] = values }, schemaRevision }
        : new { slug, values = new JsonObject { ["schemaVersion"] = 1, ["values"] = values }, schemaRevision, revision };

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid siteId, Guid collectionId, object body) =>
        client.PostAsJsonAsync(new Uri(ItemsPath(siteId, collectionId), UriKind.Relative), body);

    public static async Task<JsonElement> CreateOkAsync(HttpClient client, Guid siteId, Guid collectionId, object body) =>
        await PagesApi.OkAsync(await CreateAsync(client, siteId, collectionId, body).ConfigureAwait(false), HttpStatusCode.Created).ConfigureAwait(false);

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId) =>
        client.GetAsync(new Uri(ItemPath(siteId, collectionId, itemId), UriKind.Relative));

    public static async Task<JsonElement> GetOkAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId) =>
        await PagesApi.OkAsync(await GetAsync(client, siteId, collectionId, itemId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, object body) =>
        client.PutAsJsonAsync(new Uri(ItemPath(siteId, collectionId, itemId), UriKind.Relative), body);

    public static async Task<JsonElement> SaveOkAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, object body) =>
        await PagesApi.OkAsync(await SaveAsync(client, siteId, collectionId, itemId, body).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid siteId, Guid collectionId, string query = "") =>
        client.GetAsync(new Uri(ItemsPath(siteId, collectionId) + query, UriKind.Relative));

    public static async Task<JsonElement> ListOkAsync(HttpClient client, Guid siteId, Guid collectionId, string query = "") =>
        await PagesApi.OkAsync(await ListAsync(client, siteId, collectionId, query).ConfigureAwait(false)).ConfigureAwait(false);

    public static async Task<JsonElement> SlugAsync(HttpClient client, Guid siteId, Guid collectionId, string slug, Guid? itemId = null) =>
        await PagesApi.OkAsync(await client.GetAsync(new Uri(
            $"{ItemsPath(siteId, collectionId)}/slug-availability?slug={Uri.EscapeDataString(slug)}{(itemId is null ? string.Empty : $"&itemId={itemId}")}",
            UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, int revision) =>
        client.DeleteAsync(new Uri($"{ItemPath(siteId, collectionId, itemId)}?revision={revision}", UriKind.Relative));

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, string action, object? body = null) =>
        client.PostAsJsonAsync(new Uri($"{ItemPath(siteId, collectionId, itemId)}/{action}", UriKind.Relative), body ?? new { });

    public static Task<HttpResponseMessage> PublishAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, int revision, bool cancelSchedule = false) =>
        PostAsync(client, siteId, collectionId, itemId, "publish", new { revision, cancelSchedule });

    public static async Task<JsonElement> PublishOkAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, int revision) =>
        await PagesApi.OkAsync(await PublishAsync(client, siteId, collectionId, itemId, revision).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> UnpublishAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, int revision, bool cancelSchedule = false) =>
        PostAsync(client, siteId, collectionId, itemId, "unpublish", new { revision, cancelSchedule });

    public static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, string action, DateTimeOffset dueAtUtc, int revision, bool recapture = false) =>
        client.PutAsJsonAsync(new Uri($"{ItemPath(siteId, collectionId, itemId)}/schedules/{action}", UriKind.Relative), new { dueAtUtc, revision, recapture });

    public static Task<HttpResponseMessage> CancelScheduleAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, string action, int revision) =>
        client.DeleteAsync(new Uri($"{ItemPath(siteId, collectionId, itemId)}/schedules/{action}?revision={revision}", UriKind.Relative));

    public static Task<HttpResponseMessage> HistoryAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId) =>
        client.GetAsync(new Uri($"{ItemPath(siteId, collectionId, itemId)}/history", UriKind.Relative));

    public static Task<HttpResponseMessage> ReferrersAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, bool publishedOnly = false) =>
        client.GetAsync(new Uri($"{ItemPath(siteId, collectionId, itemId)}/referrers{(publishedOnly ? "?published=true" : string.Empty)}", UriKind.Relative));

    public static Task<HttpResponseMessage> RollbackAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, int number, int revision, bool cancelSchedule = false) =>
        PostAsync(client, siteId, collectionId, itemId, $"versions/{number}/rollback", new { revision, cancelSchedule });

    public static Task<HttpResponseMessage> CopyToDraftAsync(HttpClient client, Guid siteId, Guid collectionId, Guid itemId, int number, int revision) =>
        PostAsync(client, siteId, collectionId, itemId, $"versions/{number}/copy-to-draft", new { revision });

    public static Task<HttpResponseMessage> LookupAsync(HttpClient client, Guid siteId, IEnumerable<Guid> ids) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/cms/sites/{siteId}/items/lookup", UriKind.Relative), new { ids });

    public static int Revision(JsonElement item) => item.GetProperty("revision").GetInt32();

    public static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    public static JsonObject Values(JsonElement item, string copy = "draft") =>
        JsonNode.Parse(item.GetProperty(copy).GetProperty("values").GetRawText())!.AsObject();

    public static List<(string FieldId, string Problem)> Problems(JsonElement report) =>
        report.GetProperty("problems").EnumerateArray()
            .Select(p => (p.GetProperty("fieldId").GetString()!, p.GetProperty("problem").GetString()!))
            .ToList();
}

/// <summary>
/// The world most item tests start from: CmsScenario's two sites, a <c>people</c> collection
/// with a title, and a <c>posts</c> collection with one field of every type whose references
/// target people (CmsApi.EveryType), plus an image and a PDF in site A's library.
/// </summary>
internal sealed class ItemsScenario : IDisposable
{
    private ItemsScenario(CmsScenario cms, Guid people, Guid posts, int peopleSchema, int postsSchema, Guid image, Guid pdf)
    {
        Cms = cms;
        People = people;
        Posts = posts;
        PeopleSchemaRevision = peopleSchema;
        PostsSchemaRevision = postsSchema;
        Image = image;
        Pdf = pdf;
    }

    public CmsScenario Cms { get; }

    public HttpClient Admin => Cms.Admin;

    public Guid Site => Cms.SiteA;

    public Guid People { get; }

    public Guid Posts { get; }

    public int PeopleSchemaRevision { get; }

    public int PostsSchemaRevision { get; }

    public Guid Image { get; }

    public Guid Pdf { get; }

    public static async Task<ItemsScenario> CreateAsync(DatabaseFixture database, string label)
    {
        var cms = await CmsScenario.CreateAsync(database, label).ConfigureAwait(false);
        var people = await cms.CollectionAsync("people", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "name", "text", settings: new { multiline = false, maxLength = 100 })),
        }).ConfigureAwait(false);
        var peopleId = people.GetProperty("id").GetGuid();
        var posts = await cms.CollectionAsync("posts", CmsApi.EveryType(peopleId)).ConfigureAwait(false);

        var image = await MediaApi.UploadOkAsync(cms.Admin, cms.SiteA).ConfigureAwait(false);
        var pdf = await LibraryApi.UploadNamedAsync(cms.Admin, cms.SiteA, "brochure.pdf", SampleFiles.Pdf, "application/pdf").ConfigureAwait(false);

        return new ItemsScenario(
            cms,
            peopleId,
            posts.GetProperty("id").GetGuid(),
            people.GetProperty("schemaRevision").GetInt32(),
            posts.GetProperty("schemaRevision").GetInt32(),
            image.Id,
            pdf.Id);
    }

    /// <summary>A person, created through the API; returns the item.</summary>
    public Task<JsonElement> PersonAsync(string name, string? slug = null) =>
        ItemsApi.CreateOkAsync(Admin, Site, People, ItemsApi.Draft(
            slug ?? name.ToLowerInvariant().Replace(' ', '-'),
            new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}{name}" },
            PeopleSchemaRevision));

    /// <summary>A post with the given values (a title by default), created through the API; returns the item.</summary>
    public Task<JsonElement> PostAsync(string slug, JsonObject? values = null) =>
        ItemsApi.CreateOkAsync(Admin, Site, Posts, ItemsApi.Draft(slug, values ?? new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}{slug}" }, PostsSchemaRevision));

    public void Dispose() => Cms.Dispose();
}
