using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>Item writes are refused while a schema change holds the collection (015 FR-006, 014 FR-037).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemApiWriteGateTests(DatabaseFixture database)
{
    [Fact]
    public async Task Creating_saving_and_deleting_are_refused_while_the_schema_is_being_changed()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-gate");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);

        // Hold the collection the way a schema change does, for the duration of the checks.
        await using var scope = scenario.Cms.App.Services.CreateAsyncScope();
        var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await using (var transaction = await cms.Database.BeginTransactionAsync())
        {
            var held = await scope.ServiceProvider.GetRequiredService<CollectionLocks>().LockForUpdateAsync(scenario.Site, scenario.Posts, CancellationToken.None);
            Assert.True(held.IsSuccess);

            var refusals = new[]
            {
                await PagesApi.ProblemAsync(await ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts,
                    ItemsApi.Draft("other", new JsonObject { ["f1"] = "zz-canary-t" }, scenario.PostsSchemaRevision))),
                await PagesApi.ProblemAsync(await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                    ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-changed" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)))),
                await PagesApi.ProblemAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item))),
                await PagesApi.ProblemAsync(await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item))),
            };
            Assert.All(refusals, r => Assert.Equal("cms.items_being_updated", r.Code));

            // Reading keeps working.
            await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
            await transaction.RollbackAsync();
        }

        var unchanged = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal(ItemsApi.Revision(item), ItemsApi.Revision(unchanged));
        await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-changed" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
    }
}
