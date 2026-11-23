using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuickBuilder.Docs.Settings;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Storage;
using QuickBuilder.Shared.Infrastructure.Commands;

namespace QuickBuilder.IntegrationTests.Configuration;

/// <summary>
/// Boots the real application with a recording configuration source and compares every key it
/// reads with the documented settings, both ways (026 T042; FR-015, FR-016; SC-002;
/// contracts/setting-attributes.md §4; research OD-03).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A key the product reads that nobody declared fails: describe it with <c>[Setting]</c>,
/// or, for a framework key, add it to <c>FrameworkAllowList</c> with a reason.</item>
/// <item>A declared setting the product never reads fails: remove the dead declaration, or say
/// with <c>ObservedLazily</c> why startup does not read it.</item>
/// </list>
/// Three boots, so each storage provider's own reads are seen: the default (local storage),
/// Azure Blob against Azurite, and S3 against the S3 emulator.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class EveryReadSettingIsDeclared(DatabaseFixture database, S3Fixture s3, AzuriteFixture azurite)
    : IClassFixture<S3Fixture>, IClassFixture<AzuriteFixture>
{
    [Fact]
    public async Task Every_key_read_at_startup_is_documented_and_every_documented_key_is_read()
    {
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        observed.UnionWith(await BootAsync(new Dictionary<string, string>()));
        observed.UnionWith(await BootAsync(azurite.Configuration()));
        observed.UnionWith(await BootAsync(s3.Configuration()));

        var catalog = CatalogBuilder.BuildForProduct(RepositoryRoot());
        Assert.Empty(catalog.Problems);

        var undeclared = observed
            .Where(key => !catalog.Declares(key)
                          && !catalog.IsParentPath(key)
                          && !catalog.Exclusions.Any(e => e.Matches(key))
                          && !IsSectionOfDeclaredSetting(catalog, key))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(key => $"{key}: read by the product but not documented — add [Setting] to its options property, or add it to FrameworkAllowList with a reason.")
            .ToList();

        // A section with nothing configured is read by listing its children: the binder finds
        // none and asks for no property. Any value set there would be bound, so the setting
        // counts as read when its own key or the section or group holding it was asked for.
        var unread = catalog.Settings
            .Where(s => s.ObservedLazily is null && !ContainingPaths(s).Any(observed.Contains))
            .Select(s => $"{s.Key}: documented but never read at startup — remove the declaration, or set ObservedLazily with the reason.")
            .ToList();

        Assert.True(undeclared.Count == 0 && unread.Count == 0, string.Join('\n', undeclared.Concat(unread)));
    }

    /// <summary>The setting's own path, then each containing path up to its section's key.</summary>
    private static IEnumerable<string> ContainingPaths(SettingDefinition setting)
    {
        for (var path = setting.Path; path.Length >= setting.SectionKey.Length; path = path[..Math.Max(path.LastIndexOf(':'), 0)])
        {
            yield return path;
            if (path.Length == setting.SectionKey.Length || !path.Contains(':', StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }

    /// <summary>A section's own path is read when it is bound (e.g. <c>Storage</c>); that is not a setting.</summary>
    private static bool IsSectionOfDeclaredSetting(Catalog catalog, string key) =>
        catalog.Sections.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyCollection<string>> BootAsync(IReadOnlyDictionary<string, string> settings)
    {
        var connectionString = await database.CreateDatabaseAsync("settings-read").ConfigureAwait(false);
        var recording = new RecordingConfigurationSource();
        await using var app = new RecordingApp(connectionString, settings, recording);

        using var client = app.CreateClient();
        await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(false);
        await client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(false);

        // Host commands read configuration when they are built: build them, run none.
        using var scope = app.Services.CreateScope();
        _ = scope.ServiceProvider.GetServices<IHostCommand>().ToList();

        // Some options are bound the first time a feature needs them (email, forms, site
        // archives), not at startup: resolve every documented section as the product does.
        // A section the product never binds reads nothing here, and its keys show as unread.
        foreach (var section in CatalogBuilder.BuildForProduct(RepositoryRoot()).Sections)
        {
            var options = scope.ServiceProvider.GetService(typeof(Microsoft.Extensions.Options.IOptions<>).MakeGenericType(section.Type));
            _ = options?.GetType().GetProperty("Value")?.GetValue(options);
        }

        return recording.Keys;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    /// <summary>The real pipeline with the recording source added last; background services off.</summary>
    private sealed class RecordingApp(
        string connectionString,
        IReadOnlyDictionary<string, string> settings,
        RecordingConfigurationSource recording) : WebApplicationFactory<Program>
    {
        private readonly string _storage = QuickBuilderApp.CreateTemporaryStorage();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Storage:Local:Path", _storage);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.Add(recording));
            builder.ConfigureServices(services =>
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Npgsql.NpgsqlConnection.ClearAllPools();
                QuickBuilderApp.TryDeleteDirectory(_storage);
            }
        }
    }
}
