using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Modules;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.IntegrationTests.Isolation;

/// <summary>
/// Makes forgetting the isolation suite fail the build (009 FR-025, US5 scenarios 3 and 4).
/// </summary>
/// <remarks>
/// Two guards: every module whose model has an <see cref="ISiteOwned"/> entity has a probe,
/// and every endpoint that names a site and addresses a record is exercised by one. Each
/// guard is proven to fire on a known violation, as the architecture rules are — a guard
/// that cannot fail is not a guard.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class IsolationCoverageTests(DatabaseFixture database)
{
    /// <summary>Route values that address a record without ending in <c>Id</c>.</summary>
    internal static readonly string[] RecordKeys = ["hostName"];

    [Fact]
    public async Task Every_module_with_site_owned_data_has_a_probe()
    {
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync("iso-coverage").ConfigureAwait(true));

        var missing = MissingProbes(ModulesWithSiteOwnedData(app), IsolationProbes.All);

        Assert.True(
            missing.Count == 0,
            "These modules store site-owned records but have no isolation probe. Add one to " +
            "IsolationProbes (docs/contributing.md):\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public async Task Every_endpoint_addressing_a_record_on_a_site_is_exercised()
    {
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync("iso-endpoints").ConfigureAwait(true));

        var uncovered = UncoveredEndpoints(EndpointCatalog.Under(app, "/api"), IsolationProbes.All);

        Assert.True(
            uncovered.Count == 0,
            "These endpoints name a site and address a record, but no isolation probe sends " +
            "them another site's record:\n  " + string.Join("\n  ", uncovered));
    }

    [Fact]
    public async Task The_module_guard_fires_for_a_module_without_a_probe()
    {
        // The fixture module stores Widgets, which are site-owned, and has no probe.
        using var app = new TwoModuleApp(await database.CreateDatabaseAsync("iso-coverage-fixture").ConfigureAwait(true));

        Assert.Contains("Widgets", MissingProbes(ModulesWithSiteOwnedData(app), IsolationProbes.All));
    }

    [Fact]
    public void The_endpoint_guard_fires_for_an_unexercised_endpoint()
    {
        CallableEndpoint[] endpoints =
        [
            new("GET", "/api/v1/widgets/sites/x/widgets/y", "api/v1/widgets/sites/{siteId:guid}/widgets/{widgetId:guid}"),
            new("GET", "/api/v1/widgets/sites/x/widgets", "api/v1/widgets/sites/{siteId:guid}/widgets"),
        ];

        var uncovered = UncoveredEndpoints(endpoints, IsolationProbes.All);

        Assert.Equal(["GET api/v1/widgets/sites/{siteId:guid}/widgets/{widgetId:guid}"], uncovered);
    }

    /// <summary>The modules whose own model contains an <see cref="ISiteOwned"/> entity.</summary>
    internal static IReadOnlyList<string> ModulesWithSiteOwnedData(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        var modules = new List<string>();
        foreach (var provider in scope.ServiceProvider.GetServices<IModuleDatabaseProvider>())
        {
            using var context = provider.CreateContext(scope.ServiceProvider);
            if (context.Model.GetEntityTypes().Any(e => typeof(ISiteOwned).IsAssignableFrom(e.ClrType)))
            {
                modules.Add(provider.Describe().ModuleName);
            }
        }

        return modules;
    }

    internal static List<string> MissingProbes(IEnumerable<string> modules, IEnumerable<ISiteIsolationProbe> probes)
    {
        var probed = probes.Select(p => p.Module).ToHashSet(StringComparer.Ordinal);
        return [.. modules.Where(m => !probed.Contains(m))];
    }

    internal static List<string> UncoveredEndpoints(IEnumerable<CallableEndpoint> endpoints, IEnumerable<ISiteIsolationProbe> probes)
    {
        var covered = probes.SelectMany(p => p.CoveredEndpoints).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. endpoints
            .Where(e => AddressesARecordOnASite(e.Pattern))
            .Select(e => $"{e.Method} {e.Pattern}")
            .Where(e => !covered.Contains(e))
            .Distinct(StringComparer.Ordinal)];
    }

    private static bool AddressesARecordOnASite(string pattern)
    {
        var parameters = RouteParameter().Matches(pattern).Select(m => m.Groups["name"].Value).ToList();
        return parameters.Contains("siteId", StringComparer.Ordinal)
            && parameters.Any(p => p != "siteId"
                && (p.EndsWith("Id", StringComparison.Ordinal) || RecordKeys.Contains(p, StringComparer.Ordinal)));
    }

    private static Regex RouteParameter() => new(@"\{\*{0,2}(?<name>[A-Za-z0-9_]+)", RegexOptions.CultureInvariant);
}
