using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Kernel.MultiSite;
using Xunit;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// Every site-owned entity has the named site filter, and every trashable entity has the
/// named trash filter (FR-013; 021 TR-03).
/// </summary>
/// <remarks>
/// Filters are declared per entity in each module's <c>OnModelCreating</c>, because a
/// generic loop cannot express them correctly — see <c>SiteQueryFilterGuidance</c> for the
/// EF model-caching trap that makes the obvious helper silently filter against the wrong
/// site.
/// <para>
/// Declaring them by hand means one can be forgotten, and a forgotten filter does not fail:
/// the entity simply returns every site's rows, or every trashed row. This test inspects the
/// built models rather than the source, so an entity added without a filter fails here
/// rather than in a customer's data.
/// </para>
/// <para>
/// Since 021 every filter is named. <c>CrossSiteQuery.AcrossAllSites</c> lifts only
/// <see cref="QueryFilters.Site"/>, so trashed rows stay hidden from cross-site reads; that
/// only holds while the two filters are separate and named, which is what the third test
/// asserts.
/// </para>
/// <para>
/// The architecture tests assert the complementary half — that an entity carrying a
/// <c>SiteId</c> implements <see cref="ISiteOwned"/> in the first place.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class QueryFilterCoverageTests(DatabaseFixture database) : IAsyncLifetime
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

    [Fact]
    public void Every_site_owned_entity_has_the_named_site_filter()
    {
        var siteOwned = EntityTypes().Where(e => typeof(ISiteOwned).IsAssignableFrom(e.ClrType)).ToList();

        // A model with no site-owned entity would make this pass vacuously, which would be
        // worse than a failure — it would look like coverage.
        Assert.NotEmpty(siteOwned);

        var missing = siteOwned
            .Where(e => e.FindDeclaredQueryFilter(QueryFilters.Site) is null)
            .Select(Describe)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These entities implement ISiteOwned but carry no '" + QueryFilters.Site + "' query " +
            "filter, so they would return every site's rows:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_trashable_entity_has_the_named_trash_filter()
    {
        var trashable = EntityTypes().Where(e => typeof(ITrashable).IsAssignableFrom(e.ClrType)).ToList();

        // Pages, components, items and media files (021 TR-01), in each context that maps them.
        Assert.NotEmpty(trashable);

        var missing = trashable
            .Where(e => e.FindDeclaredQueryFilter(QueryFilters.Trash) is null)
            .Select(Describe)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These entities implement ITrashable but carry no '" + QueryFilters.Trash + "' query " +
            "filter, so trashed rows would appear in normal reads:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void No_entity_has_an_anonymous_query_filter()
    {
        var anonymous = EntityTypes()
            .Where(e => e.GetDeclaredQueryFilters().Any(f => f.IsAnonymous))
            .Select(Describe)
            .ToList();

        Assert.True(
            anonymous.Count == 0,
            "These entities declare an anonymous query filter. Name it (QueryFilters.Site or " +
            "QueryFilters.Trash) so the bypasses can lift one filter without the other:\n  " +
            string.Join("\n  ", anonymous));
    }

    /// <remarks>
    /// Every module's model, found through the providers each module registers for
    /// migration (009 FR-024). The read-only contexts used by public rendering are not
    /// registered there; they are covered by <c>TrashVisibilityTests</c>.
    /// </remarks>
    private List<IEntityType> EntityTypes()
    {
        using var scope = _app.Services.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IModuleDatabaseProvider>().ToList();
        Assert.NotEmpty(providers);

        var types = new List<IEntityType>();
        foreach (var provider in providers)
        {
            using var context = provider.CreateContext(scope.ServiceProvider);
            types.AddRange(context.Model.GetEntityTypes());
        }

        return types;
    }

    private static string Describe(IEntityType entity) => entity.ClrType.FullName!;
}
