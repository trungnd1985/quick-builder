using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// The world most Media tests start from: a claimed installation, an administrator, and two
/// sites — A on <c>site-a.test</c>, B on <c>site-b.test</c>.
/// </summary>
internal sealed class MediaScenario : IDisposable
{
    public const string HostA = "site-a.test";
    public const string HostB = "site-b.test";

    private MediaScenario(QuickBuilderApp app, HttpClient admin, Guid siteA, Guid siteB)
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

    public static async Task<MediaScenario> CreateAsync(
        DatabaseFixture database,
        string label,
        Action<IServiceCollection>? configureTestServices = null,
        string? storagePath = null,
        string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var app = new QuickBuilderApp(
            connectionString ?? await database.CreateDatabaseAsync(label).ConfigureAwait(false),
            configureTestServices: configureTestServices,
            storagePath: storagePath);

        var admin = await app.CreateAdminClientAsync().ConfigureAwait(false);
        var siteA = await MediaApi.CreateSiteAsync(admin, "Site A", HostA).ConfigureAwait(false);
        var siteB = await MediaApi.CreateSiteAsync(admin, "Site B", HostB).ConfigureAwait(false);
        return new MediaScenario(app, admin, siteA, siteB);
    }

    /// <summary>A signed-in user holding the given site roles, and their id.</summary>
    public async Task<(HttpClient Client, Guid UserId)> UserAsync(params (Guid SiteId, SystemRole Role)[] roles)
    {
        var user = await IdentitySeed.SeedUserAsync(App.Services, siteRoles: roles, displayName: "Media Tester").ConfigureAwait(false);
        var client = await IdentitySeed.CreateSignedInClientAsync(App, user.Id).ConfigureAwait(false);
        return (client, user.Id);
    }

    /// <summary>Every stored row, across all sites, read directly.</summary>
    public async Task<List<MediaFile>> RowsAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        return await database.MediaFiles
            .AcrossAllSites("Test inspection of every stored row.")
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Every committed stored object under the storage root (ignores staging and probes).</summary>
    public IReadOnlyList<string> StoredObjects() => StoredObjects(App.StoragePath);

    public static IReadOnlyList<string> StoredObjects(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".incoming" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(f => !Path.GetFileName(f).StartsWith(".probe-", StringComparison.Ordinal))
                .ToList()
            : [];

    /// <summary>The path of one file's stored object under the local root.</summary>
    public string ObjectPath(Guid siteId, Guid fileId) =>
        Path.Combine(App.StoragePath, siteId.ToString("N"), "media", fileId.ToString("N"));

    public void Dispose() => App.Dispose();
}
