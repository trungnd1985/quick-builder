using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// The one raw query of the Cms module — the row lock — applies the site itself, because no
/// global filter reaches it (014 research CS-09, Technology Constraints, Principle V).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CmsSiteFilterTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_lock_never_finds_another_sites_collection_and_one_sites_lock_never_blocks_another()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-lock-site");
        var a = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts")).GetProperty("id").GetGuid();
        var b = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteB, "posts")).GetProperty("id").GetGuid();

        await using var holderScope = scenario.App.Services.CreateAsyncScope();
        var holderDatabase = holderScope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await using var holding = await holderDatabase.Database.BeginTransactionAsync();
        var held = await holderScope.ServiceProvider.GetRequiredService<CollectionLocks>().LockForUpdateAsync(scenario.SiteA, a, CancellationToken.None);
        Assert.Equal(1, held.Value);

        await using var otherScope = scenario.App.Services.CreateAsyncScope();
        var otherDatabase = otherScope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var locks = otherScope.ServiceProvider.GetRequiredService<CollectionLocks>();
        await using var other = await otherDatabase.Database.BeginTransactionAsync();

        // Site A's collection asked for under site B: not found, not busy, so nothing leaks.
        Assert.Equal("cms.collection_not_found", (await locks.LockForUpdateAsync(scenario.SiteB, a, CancellationToken.None)).Error!.Code);

        // Site B's own collection is free while A's is held.
        Assert.True((await locks.LockForUpdateAsync(scenario.SiteB, b, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task A_lock_needs_a_transaction()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-lock-transaction");
        var a = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts")).GetProperty("id").GetGuid();

        await using var scope = scenario.App.Services.CreateAsyncScope();
        var locks = scope.ServiceProvider.GetRequiredService<CollectionLocks>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => locks.LockForUpdateAsync(scenario.SiteA, a, CancellationToken.None));
    }

    [Fact]
    public async Task Items_of_another_site_are_never_read_through_the_site_helper()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-items-site");
        var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "flag", "boolean")) };
        var a = (await scenario.CollectionAsync("posts", schema)).GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, a, 3);

        Assert.Equal(3, (await scenario.ItemsAsync(a)).Count);
        Assert.Empty(await scenario.ItemsAsync(a, scenario.SiteB));
    }
}
