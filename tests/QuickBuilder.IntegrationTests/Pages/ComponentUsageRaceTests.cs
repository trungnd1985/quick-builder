using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Deleting a component while a draft starts using it: never both (010 research P-11).
/// </summary>
/// <remarks>
/// The handlers check first, but only the composite foreign key on <c>component_usages</c>
/// can decide a true race. Each round ends in exactly one of two states, and never with a
/// usage row pointing at a deleted component.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ComponentUsageRaceTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_delete_raced_against_a_save_that_references_the_component_never_both_succeed()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-comp-race").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var page = PagesApi.PagePath(site, (await PagesApi.CreateOkAsync(admin, site, "a").ConfigureAwait(true)).GetProperty("id").GetGuid());

        for (var round = 0; round < 50; round++)
        {
            var component = await PagesApi.CreateComponentOkAsync(admin, site, $"Header {round}").ConfigureAwait(true);
            var id = component.GetProperty("id").GetGuid();
            var draftVersion = (await PagesApi.DraftOkAsync(admin, page).ConfigureAwait(true)).GetProperty("version").GetUInt32();

            using var barrier = new Barrier(2);
            var delete = Task.Run(async () =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                return await PagesApi.DeleteComponentAsync(admin, site, id, component.GetProperty("version").GetUInt32()).ConfigureAwait(false);
            });
            var save = Task.Run(async () =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                return await PagesApi.SaveDraftAsync(admin, page, PagesApi.DocumentReferencing(id), draftVersion).ConfigureAwait(false);
            });

            var (deleted, saved) = (await delete.ConfigureAwait(true), await save.ConfigureAwait(true));

            if (deleted.StatusCode == HttpStatusCode.NoContent)
            {
                Assert.Equal("pages.unknown_component", (await PagesApi.ProblemAsync(saved).ConfigureAwait(true)).Code);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
                Assert.Equal("pages.component_in_use", (await PagesApi.ProblemAsync(deleted).ConfigureAwait(true)).Code);

                // Free it again for the next round.
                await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(admin, page, PagesApi.DocumentReferencing()).ConfigureAwait(true)).ConfigureAwait(true);
            }

            var dangling = await scenario.ReadAsync(db =>
                PagesScenario.Everything<ComponentUsage>(db)
                    .CountAsync(u => !PagesScenario.Everything<Component>(db).Any(c => c.Id == u.ComponentId))).ConfigureAwait(true);
            Assert.Equal(0, dangling);
        }
    }
}
