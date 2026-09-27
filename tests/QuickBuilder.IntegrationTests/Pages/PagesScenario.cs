using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// The world most Pages tests start from: a claimed installation, an administrator, and two
/// sites — A on <c>pages-a.test</c>, B on <c>pages-b.test</c>.
/// </summary>
internal sealed class PagesScenario : IDisposable
{
    public const string HostA = "pages-a.test";
    public const string HostB = "pages-b.test";

    private PagesScenario(QuickBuilderApp app, HttpClient admin, Guid siteA, Guid siteB)
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

    public static async Task<PagesScenario> CreateAsync(DatabaseFixture database, string label)
    {
        ArgumentNullException.ThrowIfNull(database);

        var app = new QuickBuilderApp(await database.CreateDatabaseAsync(label).ConfigureAwait(false));
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(false);
        var siteA = await MediaApi.CreateSiteAsync(admin, "Site A", HostA).ConfigureAwait(false);
        var siteB = await MediaApi.CreateSiteAsync(admin, "Site B", HostB).ConfigureAwait(false);
        return new PagesScenario(app, admin, siteA, siteB);
    }

    /// <summary>A signed-in user holding the given site roles.</summary>
    public async Task<HttpClient> UserAsync(params (Guid SiteId, SystemRole Role)[] roles)
    {
        var user = await IdentitySeed.SeedUserAsync(App.Services, siteRoles: roles, displayName: "Pages Tester").ConfigureAwait(false);
        return await IdentitySeed.CreateSignedInClientAsync(App, user.Id).ConfigureAwait(false);
    }

    /// <summary>The Pages outbox's event types, oldest first, with their payloads.</summary>
    public async Task<List<(string Type, string Payload)>> OutboxAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
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

    /// <summary>Runs <paramref name="read"/> against the Pages context, across every site.</summary>
    public async Task<T> ReadAsync<T>(Func<PagesDbContext, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        await using var scope = App.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<PagesDbContext>()).ConfigureAwait(false);
    }

    public static IQueryable<T> Everything<T>(PagesDbContext database)
        where T : class =>
        database.Set<T>().AcrossAllSites("Test inspection of every stored row.");

    public void Dispose() => App.Dispose();
}
