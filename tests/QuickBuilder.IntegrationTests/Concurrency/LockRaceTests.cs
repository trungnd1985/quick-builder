using System.Net;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Domain;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Concurrency;

/// <summary>
/// At most one session holds a document's lock, whatever races for it, on one instance or on
/// two sharing the database (023 FR-010, FR-025, research CE-02).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LockRaceTests(DatabaseFixture database)
{
    private const int Members = 20;
    private const int TabsEach = 10;

    [Fact]
    public async Task Of_two_hundred_claims_at_once_exactly_one_takes_the_lock()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "lock-race-one");
        var page = await scenario.PageAsync("pricing");
        var sessions = await OpenManyAsync(scenario, [scenario.App], page);

        var responses = await Task.WhenAll(sessions.Select(s => EditingApi.ClaimAsync(s.Client, scenario.SiteA, s.SessionId)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await HoldersAsync(scenario, page));
    }

    [Fact]
    public async Task Two_instances_on_one_database_agree_on_one_holder()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "lock-race-two");
        using var second = new QuickBuilderApp(scenario.App.ConnectionString);
        var page = await scenario.PageAsync("pricing");
        var sessions = await OpenManyAsync(scenario, [scenario.App, second], page);

        var responses = await Task.WhenAll(sessions.Select(s => EditingApi.ClaimAsync(s.Client, scenario.SiteA, s.SessionId)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await HoldersAsync(scenario, page));

        // Every instance names the same holder.
        var winner = sessions[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.OK)];
        foreach (var looker in sessions.Where(s => s != winner).Take(4))
        {
            var held = EditingApi.Lock(await EditingApi.HeartbeatOkAsync(looker.Client, scenario.SiteA, looker.SessionId))!.Value;
            Assert.Equal(winner.SessionId, held.GetProperty("holderSessionId").GetGuid());
        }
    }

    [Fact]
    public async Task Of_ten_takeovers_of_one_stale_lock_at_once_exactly_one_succeeds_and_one_is_recorded()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "lock-race-takeover");
        var page = await scenario.PageAsync("pricing");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var annas = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page));
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, annas);

        var takers = new List<Opened>();
        for (var m = 0; m < 10; m++)
        {
            var (client, _) = await scenario.MemberAsync($"Taker {m}", scenario.SiteA, SystemRole.Designer);
            takers.Add(new Opened(client, EditingApi.SessionId(await EditingApi.OpenOkAsync(client, scenario.SiteA, "page", page))));
        }

        // Thirty-one minutes with everyone present and Anna doing nothing.
        for (var i = 0; i < 31; i++)
        {
            scenario.Clock.Advance(TimeSpan.FromMinutes(1));
            await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, annas);
            await Task.WhenAll(takers.Select(t => EditingApi.HeartbeatOkAsync(t.Client, scenario.SiteA, t.SessionId)));
        }

        var responses = await Task.WhenAll(takers.Select(t => EditingApi.TakeOverAsync(t.Client, scenario.SiteA, t.SessionId, annas)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await HoldersAsync(scenario, page));
        Assert.Equal(1, await scenario.ReadAsync(d => EditingScenario.Everything<EditingTakeover>(d).CountAsync()));
    }

    private sealed record Opened(HttpClient Client, Guid SessionId);

    /// <summary>Twenty Designers with ten tabs each, spread over the given instances, all viewing the page.</summary>
    private static async Task<List<Opened>> OpenManyAsync(EditingScenario scenario, IReadOnlyList<QuickBuilderApp> instances, Guid page)
    {
        var opened = new List<Opened>();
        for (var m = 0; m < Members; m++)
        {
            var user = await IdentitySeed.SeedUserAsync(scenario.App.Services, siteRoles: [(scenario.SiteA, SystemRole.Designer)], displayName: $"Member {m}");
            for (var t = 0; t < TabsEach; t++)
            {
                var client = await IdentitySeed.CreateSignedInClientAsync(instances[(m * TabsEach + t) % instances.Count], user.Id);
                opened.Add(new Opened(client, EditingApi.SessionId(await EditingApi.OpenOkAsync(client, scenario.SiteA, "page", page))));
            }
        }

        return opened;
    }

    private static Task<int> HoldersAsync(EditingScenario scenario, Guid page) =>
        scenario.ReadAsync(d => EditingScenario.Everything<EditingSession>(d).CountAsync(s => s.DocumentId == page && s.HoldsLock));
}
