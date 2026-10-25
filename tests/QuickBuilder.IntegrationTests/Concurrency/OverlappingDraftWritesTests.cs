using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Concurrency;

/// <summary>
/// No draft save ever overwrites someone else's change (023 SC-001, FR-001 to FR-003): pairs
/// of overlapping writes from two members, on the same base version, over pages, shared
/// components and collection items.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OverlappingDraftWritesTests(DatabaseFixture database)
{
    private const int Pairs = 1_000;
    private const int DocumentsPerKind = 8;

    [Fact]
    public async Task Of_every_pair_of_overlapping_draft_writes_exactly_one_is_stored_and_the_other_names_it()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "concurrency-overlap");
        var app = scenario.Cms.App;
        var site = scenario.Site;

        var annaUser = await IdentitySeed.SeedUserAsync(app.Services, siteRoles: [(site, SystemRole.Designer)], displayName: "Anna Tran");
        var benUser = await IdentitySeed.SeedUserAsync(app.Services, siteRoles: [(site, SystemRole.Designer)], displayName: "Ben Hale");
        var anna = await IdentitySeed.CreateSignedInClientAsync(app, annaUser.Id);
        var ben = await IdentitySeed.CreateSignedInClientAsync(app, benUser.Id);
        var members = new[] { (Client: anna, User: annaUser.Id, Name: "Anna Tran"), (Client: ben, User: benUser.Id, Name: "Ben Hale") };

        var documents = new List<Func<int, Task<PairOutcome>>>();
        for (var i = 0; i < DocumentsPerKind; i++)
        {
            var page = PagesApi.PagePath(site, ItemsApi.Id(await PagesApi.CreateOkAsync(scenario.Admin, site, $"overlap-{i}")));
            var component = PagesApi.ComponentPath(site, ItemsApi.Id(await PagesApi.CreateComponentOkAsync(scenario.Admin, site, $"Overlap {i}")));
            var item = ItemsApi.Id(await scenario.PostAsync($"overlap-{i}"));

            documents.Add(round => PageLikePairAsync(members, page, round));
            documents.Add(round => PageLikePairAsync(members, component, round));
            documents.Add(round => ItemPairAsync(members, scenario, item, round));
        }

        var outcomes = new List<PairOutcome>();
        for (var round = 0; outcomes.Count < Pairs; round++)
        {
            var batch = documents.Take(Pairs - outcomes.Count).Select(pair => pair(round));
            outcomes.AddRange(await Task.WhenAll(batch));
        }

        Assert.Equal(Pairs, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Ok, o.Failure));
    }

    private sealed record PairOutcome(bool Ok, string Failure)
    {
        public static PairOutcome Pass { get; } = new(true, string.Empty);
    }

    private static async Task<PairOutcome> PageLikePairAsync((HttpClient Client, Guid User, string Name)[] members, string owner, int round)
    {
        var version = (await PagesApi.DraftOkAsync(members[0].Client, owner)).GetProperty("version").GetUInt32();
        var documents = members.Select((_, m) => PagesApi.DocumentWithText($"zz-canary-{round}-{m}")).ToArray();

        var responses = await Task.WhenAll(members.Select((member, m) => PagesApi.SaveDraftAsync(member.Client, owner, documents[m], version)));
        return await JudgeAsync(members, responses, async () =>
        {
            var stored = (await PagesApi.DraftOkAsync(members[0].Client, owner)).GetProperty("document");
            return documents.Select(d => JsonDocument.Parse(d).RootElement).ToList().FindIndex(d => JsonElement.DeepEquals(d, stored));
        }, "pages.out_of_date");
    }

    private static async Task<PairOutcome> ItemPairAsync((HttpClient Client, Guid User, string Name)[] members, ItemsScenario scenario, Guid item, int round)
    {
        var current = await ItemsApi.GetOkAsync(members[0].Client, scenario.Site, scenario.Posts, item);
        var draftRevision = current.GetProperty("draftRevision").GetInt32();
        var titles = members.Select((_, m) => $"zz-canary-{round}-{m}").ToArray();

        var responses = await Task.WhenAll(members.Select((member, m) => ItemsApi.SaveAsync(member.Client, scenario.Site, scenario.Posts, item,
            ItemsApi.DraftOf(current.GetProperty("slug").GetString()!, new JsonObject { ["f1"] = titles[m] }, scenario.PostsSchemaRevision, draftRevision))));
        return await JudgeAsync(members, responses, async () =>
        {
            var stored = ItemsApi.Values(await ItemsApi.GetOkAsync(members[0].Client, scenario.Site, scenario.Posts, item))["f1"]!.GetValue<string>();
            return Array.IndexOf(titles, stored);
        }, "cms.item_conflict");
    }

    /// <summary>One save stored, the other refused naming the one stored, and the draft equal to the stored one.</summary>
    private static async Task<PairOutcome> JudgeAsync(
        (HttpClient Client, Guid User, string Name)[] members, HttpResponseMessage[] responses, Func<Task<int>> storedIndex, string conflictCode)
    {
        var winners = Enumerable.Range(0, responses.Length).Where(i => responses[i].StatusCode == HttpStatusCode.OK).ToList();
        if (winners.Count != 1)
        {
            return new PairOutcome(false, $"Expected exactly one stored write, got {winners.Count}: {string.Join(", ", responses.Select(r => (int)r.StatusCode))}");
        }

        var winner = winners[0];
        var loser = 1 - winner;
        var problem = await PagesApi.ProblemAsync(responses[loser]);
        if (problem.Status != HttpStatusCode.Conflict || problem.Code != conflictCode)
        {
            return new PairOutcome(false, $"Expected 409 {conflictCode}, got {(int)problem.Status} {problem.Code}");
        }

        var changedBy = problem.Body.GetProperty("changedBy");
        if (changedBy.GetProperty("id").GetGuid() != members[winner].User || changedBy.GetProperty("displayName").GetString() != members[winner].Name)
        {
            return new PairOutcome(false, $"The refusal names {changedBy}, not the member whose save was stored.");
        }

        if (!problem.Body.TryGetProperty("changedAtUtc", out _))
        {
            return new PairOutcome(false, "The refusal has no changedAtUtc.");
        }

        var stored = await storedIndex();
        return stored == winner
            ? PairOutcome.Pass
            : new PairOutcome(false, $"The draft holds write {stored}, but write {winner} was the one accepted.");
    }
}
