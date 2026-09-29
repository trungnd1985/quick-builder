using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>Requests of 012 contracts/publishing-api.yaml, for tests.</summary>
internal static class PublishingApi
{
    public static string Subject(Guid siteId, string kind, Guid id) =>
        kind == "page" ? PagesApi.PagePath(siteId, id) : PagesApi.ComponentPath(siteId, id);

    public static async Task<uint> DraftVersionAsync(HttpClient client, string subjectPath) =>
        (await PagesApi.DraftOkAsync(client, subjectPath).ConfigureAwait(false)).GetProperty("version").GetUInt32();

    public static Task<HttpResponseMessage> PublishAsync(HttpClient client, string subjectPath, uint draftVersion) =>
        client.PostAsJsonAsync(new Uri($"{subjectPath}/publish", UriKind.Relative), new { draftVersion });

    /// <summary>Publishes the current draft and returns the publication.</summary>
    public static async Task<JsonElement> PublishCurrentAsync(HttpClient client, string subjectPath)
    {
        var version = await DraftVersionAsync(client, subjectPath).ConfigureAwait(false);
        return await PagesApi.OkAsync(await PublishAsync(client, subjectPath, version).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public static Task<HttpResponseMessage> UnpublishAsync(HttpClient client, string subjectPath) =>
        client.PostAsync(new Uri($"{subjectPath}/unpublish", UriKind.Relative), content: null);

    public static Task<HttpResponseMessage> RollbackAsync(HttpClient client, string subjectPath, int versionNumber) =>
        client.PostAsJsonAsync(new Uri($"{subjectPath}/rollback", UriKind.Relative), new { versionNumber });

    public static Task<HttpResponseMessage> HistoryAsync(HttpClient client, string subjectPath) =>
        client.GetAsync(new Uri($"{subjectPath}/history", UriKind.Relative));

    public static Task<HttpResponseMessage> VersionAsync(HttpClient client, string subjectPath, int number) =>
        client.GetAsync(new Uri($"{subjectPath}/versions/{number}", UriKind.Relative));

    public static Task<HttpResponseMessage> CopyToDraftAsync(HttpClient client, string subjectPath, int number, uint draftVersion) =>
        client.PostAsJsonAsync(new Uri($"{subjectPath}/versions/{number}/copy-to-draft", UriKind.Relative), new { draftVersion });

    public static Task<HttpResponseMessage> ScheduleAsync(
        HttpClient client, string subjectPath, string action, DateTimeOffset dueAtUtc, uint? draftVersion = null) =>
        client.PutAsJsonAsync(new Uri($"{subjectPath}/schedules/{action}", UriKind.Relative), new { dueAtUtc, draftVersion });

    public static Task<HttpResponseMessage> CancelScheduleAsync(HttpClient client, string subjectPath, string action) =>
        client.DeleteAsync(new Uri($"{subjectPath}/schedules/{action}", UriKind.Relative));

    public static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid siteId, string query = "") =>
        client.GetAsync(new Uri($"/api/v1/pages/sites/{siteId}/publish-log{query}", UriKind.Relative));

    /// <summary>A page document with a Section holding a Heading with <paramref name="heading"/>.</summary>
    public static string HeadingDocument(string heading, params Guid[] componentIds)
    {
        var refs = string.Concat(componentIds.Select((id, i) =>
            $$"""{"id":"ref{{i}}","type":"component-ref","props":{"componentId":"{{id}}"},"children":[]},"""));
        var text = JsonSerializer.Serialize(heading);

        return """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":["""
            + refs
            + """{"id":"s1","type":"section","props":{},"children":[{"id":"h1","type":"heading","props":{"text":"""
            + text
            + ""","level":1},"children":[]}]}]}}""";
    }

    /// <summary>A component document: one Section with a Text.</summary>
    public static string ComponentDocument(string text) =>
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"cs","type":"section","props":{},"children":[{"id":"ct","type":"text","props":{"text":"""
        + JsonSerializer.Serialize(text)
        + """},"children":[]}]}]}}""";

    /// <summary>Saves a document over the current draft.</summary>
    public static async Task SaveAsync(HttpClient client, string subjectPath, string document)
    {
        var response = await PagesApi.SaveCurrentDraftAsync(client, subjectPath, document).ConfigureAwait(false);
        await PagesApi.OkAsync(response, HttpStatusCode.OK).ConfigureAwait(false);
    }
}
