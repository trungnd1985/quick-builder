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

    /// <summary>
    /// Every table 015 adds is filtered by site, and the one cross-site read that background
    /// indexing needs returns ids paired with their own site (015 data-model §1, research IT-22).
    /// </summary>
    [Fact]
    public async Task Item_tables_of_another_site_are_never_read_and_indexing_work_is_paired_with_its_site()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-item-tables-site");
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 })),
        };
        var a = (await scenario.CollectionAsync("posts", schema)).GetProperty("id").GetGuid();
        var b = (await scenario.CollectionAsync("posts", schema, scenario.SiteB)).GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, a, 2, published: true);
        await ItemSeed.AddAsync(scenario, scenario.SiteB, b, 1);

        var work = await scenario.WithDatabaseAsync(cms => cms.CollectionsToIndexAsync(int.MaxValue, CancellationToken.None));
        Assert.Equal(
            [new SiteCollection(scenario.SiteA, a), new SiteCollection(scenario.SiteB, b)],
            work.OrderBy(w => w.SiteId == scenario.SiteB).ToList());

        await scenario.App.Services.GetRequiredService<ItemIndexBackfillService>().RunOnceAsync(CancellationToken.None);

        var counts = await scenario.WithDatabaseAsync(async cms => new[]
        {
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemSlug>(scenario.SiteB).CountAsync(),
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemSlug>(scenario.SiteA).CountAsync(),
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemVersion>(scenario.SiteB).CountAsync(),
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemReference>(scenario.SiteB).CountAsync(),
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemMediaUse>(scenario.SiteB).CountAsync(),
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemSchedule>(scenario.SiteB).CountAsync(),
            await cms.ForSite<QuickBuilder.Cms.Domain.ItemHistoryEntry>(scenario.SiteB).CountAsync(),

            // Without a site, the global filters match nothing at all.
            await cms.ItemSlugs.CountAsync(),
            await cms.ItemVersions.CountAsync(),
        });

        Assert.Equal([1, 2, 0, 0, 0, 0, 0, 0, 0], counts);
    }
}
