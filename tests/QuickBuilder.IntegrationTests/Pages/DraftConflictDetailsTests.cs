using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// An out-of-date draft save names who saved the newer version, when, and its version (023
/// FR-003, research CE-09). The code is unchanged, so older admins still recognise it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DraftConflictDetailsTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_page_and_a_component_conflict_name_the_member_time_and_version_of_the_stored_draft()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-conflict-details");
        var ben = await IdentitySeed.SeedUserAsync(scenario.App.Services, siteRoles: [(scenario.SiteA, SystemRole.Designer)], displayName: "Ben Hale");
        var benClient = await IdentitySeed.CreateSignedInClientAsync(scenario.App, ben.Id);

        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "pricing")).GetProperty("id").GetGuid());
        var component = PagesApi.ComponentPath(scenario.SiteA, (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid());

        foreach (var owner in new[] { page, component })
        {
            var loaded = (await PagesApi.DraftOkAsync(scenario.Admin, owner)).GetProperty("version").GetUInt32();
            var before = DateTimeOffset.UtcNow.AddSeconds(-5);

            var saved = await PagesApi.OkAsync(await PagesApi.SaveDraftAsync(benClient, owner, PagesApi.DocumentWithText("zz-canary-ben"), loaded));
            var refused = await PagesApi.RefusedAsync(
                await PagesApi.SaveDraftAsync(scenario.Admin, owner, PagesApi.DocumentWithText("zz-canary-admin"), loaded),
                HttpStatusCode.Conflict,
                "pages.out_of_date");

            var changedBy = refused.Body.GetProperty("changedBy");
            Assert.Equal(ben.Id, changedBy.GetProperty("id").GetGuid());
            Assert.Equal("Ben Hale", changedBy.GetProperty("displayName").GetString());
            Assert.InRange(refused.Body.GetProperty("changedAtUtc").GetDateTimeOffset(), before, DateTimeOffset.UtcNow.AddSeconds(5));
            Assert.Equal(saved.GetProperty("version").GetUInt32(), refused.Body.GetProperty("currentVersion").GetUInt32());

            var stored = await PagesApi.DraftOkAsync(scenario.Admin, owner);
            Assert.Contains("zz-canary-ben", stored.GetProperty("document").GetRawText(), StringComparison.Ordinal);
        }
    }
}
