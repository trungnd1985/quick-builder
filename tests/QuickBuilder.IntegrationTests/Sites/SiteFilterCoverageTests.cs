using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Infrastructure.Modules;
using Xunit;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// Every site-owned entity actually has a query filter (FR-013).
/// </summary>
/// <remarks>
/// Site filters are declared per entity in each module's <c>OnModelCreating</c>, because
/// a generic loop cannot express them correctly — see <c>SiteQueryFilterGuidance</c> for
/// the EF model-caching trap that makes the obvious helper silently filter against the
/// wrong site.
/// <para>
/// Declaring them by hand means one can be forgotten, and a forgotten filter does not
/// fail: the entity simply returns every site's rows. This test closes that hole by
/// inspecting the built model rather than the source, so an entity added without a filter
/// fails here rather than in a customer's data.
/// </para>
/// <para>
/// The architecture tests assert the complementary half — that an entity carrying a
/// <c>SiteId</c> implements <see cref="ISiteOwned"/> in the first place.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SiteFilterCoverageTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("filtercover").ConfigureAwait(true);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Every module's model, found through the providers each module registers for
    /// migration (009 FR-024). Until 009 this looked at the Sites model only, so a second
    /// module's forgotten filter would have passed.
    /// </remarks>
    [Fact]
    public void Every_site_owned_entity_has_a_query_filter()
    {
        using var scope = _app.Services.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IModuleDatabaseProvider>().ToList();
        Assert.NotEmpty(providers);

        var siteOwned = new List<Microsoft.EntityFrameworkCore.Metadata.IEntityType>();
        foreach (var provider in providers)
        {
            using var context = provider.CreateContext(scope.ServiceProvider);
            siteOwned.AddRange(context.Model
                .GetEntityTypes()
                .Where(e => typeof(ISiteOwned).IsAssignableFrom(e.ClrType)));
        }

        // A module with no site-owned entity would make this pass vacuously, which would
        // be worse than a failure — it would look like coverage.
        Assert.NotEmpty(siteOwned);

        var unfiltered = siteOwned
            .Where(e => e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.FullName!)
            .ToList();

        Assert.True(
            unfiltered.Count == 0,
            "These entities implement ISiteOwned but carry no query filter, so they would " +
            "return every site's rows:\n  " + string.Join("\n  ", unfiltered));
    }
}
