using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// The world most Cms tests start from: a claimed installation, an administrator, and two
/// sites — A on <c>cms-a.test</c>, B on <c>cms-b.test</c>.
/// </summary>
internal sealed class CmsScenario : IDisposable
{
    public const string HostA = "cms-a.test";
    public const string HostB = "cms-b.test";

    private CmsScenario(QuickBuilderApp app, HttpClient admin, Guid siteA, Guid siteB)
    {
        App = app;
        Admin = admin;
        SiteA = siteA;
        SiteB = siteB;
    }

    public QuickBuilderApp App { get; }

    public HttpClient Admin { get; }

    public Guid SiteA { get; }

    public Guid SiteB { get; }

    public static async Task<CmsScenario> CreateAsync(DatabaseFixture database, string label, Action<IServiceCollection>? configureTestServices = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var app = new QuickBuilderApp(await database.CreateDatabaseAsync(label).ConfigureAwait(false), configureTestServices: configureTestServices);
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(false);
        var siteA = await MediaApi.CreateSiteAsync(admin, "Site A", HostA).ConfigureAwait(false);
        var siteB = await MediaApi.CreateSiteAsync(admin, "Site B", HostB).ConfigureAwait(false);
        return new CmsScenario(app, admin, siteA, siteB);
    }

    /// <summary>A signed-in user holding the given site roles.</summary>
    public async Task<HttpClient> UserAsync(params (Guid SiteId, SystemRole Role)[] roles)
    {
        var user = await IdentitySeed.SeedUserAsync(App.Services, siteRoles: roles, displayName: "Cms Tester").ConfigureAwait(false);
        return await IdentitySeed.CreateSignedInClientAsync(App, user.Id).ConfigureAwait(false);
    }

    /// <summary>A collection on site A whose schema is <paramref name="schema"/> (temporary ids), saved; returns it.</summary>
    public async Task<JsonElement> CollectionAsync(string slug, JsonObject schema, Guid? site = null)
    {
        var siteId = site ?? SiteA;
        var created = await CmsApi.CreateOkAsync(Admin, siteId, slug).ConfigureAwait(false);
        return await CmsApi.SaveOkAsync(Admin, siteId, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema)).ConfigureAwait(false);
    }

    /// <summary>The Cms outbox's event types, oldest first, with their payloads.</summary>
    public async Task<List<(string Type, string Payload)>> OutboxAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        return (await database.Set<OutboxMessage>()
                .AsNoTracking()
                .OrderBy(m => m.OccurredAtUtc)
                .ThenBy(m => m.Id)
                .Select(m => new { m.EventType, m.Payload })
                .ToListAsync()
                .ConfigureAwait(false))
            .Select(m => (m.EventType, m.Payload))
            .ToList();
    }

    /// <summary>Runs <paramref name="work"/> against the Cms context, for one site.</summary>
    public async Task<T> WithDatabaseAsync<T>(Func<CmsDbContext, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = App.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<CmsDbContext>()).ConfigureAwait(false);
    }

    /// <summary>Every item of a collection: id, draft and published values as JSON text, in id order.</summary>
    public Task<List<(Guid Id, string Draft, string? Published)>> ItemsAsync(Guid collectionId, Guid? site = null) =>
        WithDatabaseAsync(async database => (await database.ForSite<Item>(site ?? SiteA)
                .AsNoTracking()
                .Where(i => i.CollectionId == collectionId)
                .OrderBy(i => i.Id)
                .ToListAsync()
                .ConfigureAwait(false))
            .Select(i => (i.Id, i.Draft.RootElement.GetRawText(), i.Published?.RootElement.GetRawText()))
            .ToList());

    /// <summary>Changes one item's draft values directly, as #15's editor will: to make a preview stale.</summary>
    public Task<bool> EditItemAsync(Guid itemId, Action<ItemValues> edit, Guid? site = null) =>
        WithDatabaseAsync(async database =>
        {
            var item = await database.ForSite<Item>(site ?? SiteA).SingleAsync(i => i.Id == itemId).ConfigureAwait(false);
            var draft = item.DraftValues();
            edit(draft);
            item.Migrate(draft, item.PublishedValues(), TimeProvider.System);
            await database.SaveChangesAsync().ConfigureAwait(false);
            return true;
        });

    public void Dispose()
    {
        Admin.Dispose();
        App.Dispose();
    }
}
