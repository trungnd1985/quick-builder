using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>Drives the editing-session API (023 contracts/editing-api.yaml).</summary>
internal static class EditingApi
{
    public static string SessionsPath(Guid siteId) => $"/api/v1/editor/sites/{siteId}/editing-sessions";

    public static string SessionPath(Guid siteId, Guid sessionId) => $"{SessionsPath(siteId)}/{sessionId}";

    public static Task<HttpResponseMessage> OpenAsync(HttpClient client, Guid siteId, string kind, Guid documentId, Guid? collectionId = null) =>
        client.PostAsJsonAsync(new Uri(SessionsPath(siteId), UriKind.Relative), new { kind, documentId, collectionId });

    public static async Task<JsonElement> OpenOkAsync(HttpClient client, Guid siteId, string kind, Guid documentId, Guid? collectionId = null) =>
        await PagesApi.OkAsync(await OpenAsync(client, siteId, kind, documentId, collectionId).ConfigureAwait(false), HttpStatusCode.Created).ConfigureAwait(false);

    public static Task<HttpResponseMessage> HeartbeatAsync(HttpClient client, Guid siteId, Guid sessionId, bool active = false) =>
        client.PostAsJsonAsync(new Uri($"{SessionPath(siteId, sessionId)}/heartbeat", UriKind.Relative), new { active });

    public static async Task<JsonElement> HeartbeatOkAsync(HttpClient client, Guid siteId, Guid sessionId, bool active = false) =>
        await PagesApi.OkAsync(await HeartbeatAsync(client, siteId, sessionId, active).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid siteId, Guid sessionId) =>
        client.PostAsync(new Uri($"{SessionPath(siteId, sessionId)}/claim", UriKind.Relative), null);

    public static async Task<JsonElement> ClaimOkAsync(HttpClient client, Guid siteId, Guid sessionId) =>
        await PagesApi.OkAsync(await ClaimAsync(client, siteId, sessionId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Task<HttpResponseMessage> TakeOverAsync(HttpClient client, Guid siteId, Guid sessionId, Guid holderSessionId) =>
        client.PostAsJsonAsync(new Uri($"{SessionPath(siteId, sessionId)}/takeover", UriKind.Relative), new { holderSessionId });

    public static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid siteId, Guid sessionId, Guid holderSessionId) =>
        client.PostAsJsonAsync(new Uri($"{SessionPath(siteId, sessionId)}/move", UriKind.Relative), new { holderSessionId });

    public static Task<HttpResponseMessage> ReleaseAsync(HttpClient client, Guid siteId, Guid sessionId) =>
        client.DeleteAsync(new Uri(SessionPath(siteId, sessionId), UriKind.Relative));

    public static Task<HttpResponseMessage> HoldersAsync(HttpClient client, Guid siteId, string kind, Guid? collectionId = null) =>
        client.GetAsync(new Uri($"/api/v1/editor/sites/{siteId}/editing?kind={kind}{(collectionId is { } c ? $"&collectionId={c}" : string.Empty)}", UriKind.Relative));

    public static async Task<JsonElement> HoldersOkAsync(HttpClient client, Guid siteId, string kind, Guid? collectionId = null) =>
        await PagesApi.OkAsync(await HoldersAsync(client, siteId, kind, collectionId).ConfigureAwait(false)).ConfigureAwait(false);

    public static Guid SessionId(JsonElement presence) => presence.GetProperty("sessionId").GetGuid();

    public static string Role(JsonElement presence) => presence.GetProperty("role").GetString()!;

    /// <summary>The holder in a presence, or null.</summary>
    public static JsonElement? Lock(JsonElement presence) =>
        presence.TryGetProperty("lock", out var held) && held.ValueKind == JsonValueKind.Object ? held : null;
}

/// <summary>
/// The world the editing tests start from: an installation with a hand-moved clock, site A
/// with a page, and members named Anna and Ben (Designers on A), Edna (Editor on A) and
/// Bea (Designer on site B only).
/// </summary>
internal sealed class EditingScenario : IDisposable
{
    private EditingScenario(QuickBuilderApp app, ManualClock clock, HttpClient admin, Guid siteA, Guid siteB)
    {
        App = app;
        Clock = clock;
        Admin = admin;
        SiteA = siteA;
        SiteB = siteB;
    }

    public QuickBuilderApp App { get; }

    public ManualClock Clock { get; }

    public HttpClient Admin { get; }

    public Guid SiteA { get; }

    public Guid SiteB { get; }

    public static async Task<EditingScenario> CreateAsync(DatabaseFixture database, string label, string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var clock = new ManualClock();
        var app = new QuickBuilderApp(
            connectionString ?? await database.CreateDatabaseAsync(label).ConfigureAwait(false),
            configureTestServices: s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(false);
        var siteA = await MediaApi.CreateSiteAsync(admin, "Site A", $"{label}-a.test").ConfigureAwait(false);
        var siteB = await MediaApi.CreateSiteAsync(admin, "Site B", $"{label}-b.test").ConfigureAwait(false);
        return new EditingScenario(app, clock, admin, siteA, siteB);
    }

    /// <summary>A signed-in member with one role on one site; returns the client and the user id.</summary>
    public async Task<(HttpClient Client, Guid Id)> MemberAsync(string name, Guid siteId, SystemRole role)
    {
        var user = await IdentitySeed.SeedUserAsync(App.Services, siteRoles: [(siteId, role)], displayName: name).ConfigureAwait(false);
        return (await IdentitySeed.CreateSignedInClientAsync(App, user.Id).ConfigureAwait(false), user.Id);
    }

    /// <summary>A second signed-in client for the same member: another tab or browser.</summary>
    public Task<HttpClient> AnotherSessionAsync(Guid userId) => IdentitySeed.CreateSignedInClientAsync(App, userId);

    public async Task<Guid> PageAsync(string slug) =>
        (await PagesApi.CreateOkAsync(Admin, SiteA, slug).ConfigureAwait(false)).GetProperty("id").GetGuid();

    /// <summary>Runs <paramref name="read"/> against the Editor context, across every site.</summary>
    public async Task<T> ReadAsync<T>(Func<EditorDbContext, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        await using var scope = App.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<EditorDbContext>()).ConfigureAwait(false);
    }

    public static IQueryable<T> Everything<T>(EditorDbContext database)
        where T : class =>
        database.Set<T>().AcrossAllSites("Test inspection of every stored row.");

    public void Dispose() => App.Dispose();
}
