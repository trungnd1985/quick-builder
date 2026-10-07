using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// While a schema change runs, item writes are refused at once and readers see the items as
/// they were, never a mix; while an item write runs, a schema change is refused
/// (014 FR-037, research CS-10, clarification Q3).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemWriteGateTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_item_write_during_a_schema_change_is_refused_and_readers_see_before_then_after()
    {
        using var observer = new TestMigrationObserver();
        using var scenario = await CmsScenario.CreateAsync(database, "cms-gate", TestMigrationObserver.Install(observer));
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
                CmsApi.Field("~2", "flag", "boolean")),
        };
        var collection = await scenario.CollectionAsync("posts", schema);
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 1_200);
        var before = await scenario.ItemsAsync(id);

        // Remove the flag; hold the change after its first batch of 500.
        var removed = CmsApi.Schema(collection);
        CmsApi.Fields(removed).RemoveAt(1);
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removed));
        observer.HoldAfterBatch = 1;
        var saving = CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removed, digest: preview.GetProperty("digest").GetString()));
        await observer.WaitUntilHeldAsync();

        try
        {
            await using (var scope = scenario.App.Services.CreateAsyncScope())
            {
                var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
                await using var transaction = await cms.Database.BeginTransactionAsync();
                var entered = await scope.ServiceProvider.GetRequiredService<CollectionWriteGate>().EnterAsync(scenario.SiteA, id, null, CancellationToken.None);
                Assert.Equal("cms.items_being_updated", entered.Error!.Code);
            }

            // Readers are not blocked, and see every item as it was before the change.
            Assert.Equal(before, await scenario.ItemsAsync(id));

            // A second schema change is refused too.
            var second = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removed)));
            Assert.Equal("cms.collection_busy", second.Code);
        }
        finally
        {
            observer.Release();
        }

        await PagesApi.OkAsync(await saving);
        Assert.All(await scenario.ItemsAsync(id), i => Assert.DoesNotContain("\"f2\"", i.Draft, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_schema_change_while_an_item_write_holds_the_gate_is_busy()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-gate-held");
        var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "flag", "boolean")) };
        var collection = await scenario.CollectionAsync("posts", schema);
        var id = collection.GetProperty("id").GetGuid();

        await using var scope = scenario.App.Services.CreateAsyncScope();
        var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await using var transaction = await cms.Database.BeginTransactionAsync();
        var entered = await scope.ServiceProvider.GetRequiredService<CollectionWriteGate>().EnterAsync(scenario.SiteA, id, 2, CancellationToken.None);
        Assert.Equal((2, "f1"), (entered.Value.Revision, entered.Value.Schema.Fields[0].Id));

        var renamed = CmsApi.Schema(collection);
        CmsApi.Fields(renamed)[0]!["name"] = "Featured";
        var busy = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, renamed)));
        Assert.Equal("cms.collection_busy", busy.Code);

        await transaction.RollbackAsync();
        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, renamed));
    }
}
