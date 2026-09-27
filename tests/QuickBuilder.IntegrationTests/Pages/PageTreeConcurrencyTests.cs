using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Concurrent structural changes never corrupt a tree (010 SC-002, research P-05).
/// </summary>
/// <remarks>
/// Each race is started with a barrier so the requests overlap as much as the pipeline
/// allows. The outcome that matters is the stored tree afterwards, not which request won.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PageTreeConcurrencyTests(DatabaseFixture database)
{
    [Fact]
    public async Task Parallel_creates_of_one_address_give_exactly_one_page()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-race-create").ConfigureAwait(true);
        await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true);

        var responses = await RaceAsync(10, () => PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, "about")).ConfigureAwait(true);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            var problem = await PagesApi.ProblemAsync(refused).ConfigureAwait(true);
            Assert.Contains(problem.Code, new[] { "pages.duplicate_address", "pages.out_of_date" });
        }

        Assert.Equal(["/", "/about"], await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true));
    }

    [Fact]
    public async Task Parallel_first_pages_on_an_empty_site_never_fail_unexpectedly()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-race-first").ConfigureAwait(true);

        var slugs = Enumerable.Range(0, 6).Select(i => $"p{i}").ToArray();
        var index = -1;
        var responses = await RaceAsync(slugs.Length, () => PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, slugs[Interlocked.Increment(ref index)]))
            .ConfigureAwait(true);

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"{(int)r.StatusCode}"));
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(created, (await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true)).Length);
        Assert.True(created >= 1);
    }

    [Fact]
    public async Task Crossed_moves_never_make_a_cycle()
    {
        for (var round = 0; round < 5; round++)
        {
            using var scenario = await PagesScenario.CreateAsync(database, "pg-race-cycle").ConfigureAwait(true);
            var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
                a
                b
                """).ConfigureAwait(true);

            var moves = new[]
            {
                () => PagesApi.MoveAsync(scenario.Admin, scenario.SiteA, ids["/a"], ids["/b"], null, 1),
                () => PagesApi.MoveAsync(scenario.Admin, scenario.SiteA, ids["/b"], ids["/a"], null, 1),
            };
            var index = -1;
            var responses = await RaceAsync(2, () => moves[Interlocked.Increment(ref index)]()).ConfigureAwait(true);

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            var paths = await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);
            Assert.True(paths.SequenceEqual(["/a", "/a/b"]) || paths.SequenceEqual(["/b", "/b/a"]), string.Join(", ", paths));
        }
    }

    [Fact]
    public async Task A_reslug_raced_against_a_create_at_the_same_address_leaves_one_page_there()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-race-reslug").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "old").ConfigureAwait(true);

        var actions = new[]
        {
            () => PagesApi.UpdateAsync(scenario.Admin, scenario.SiteA, ids["/old"], "t", "new", 1),
            () => PagesApi.CreateAsync(scenario.Admin, scenario.SiteA, "new"),
        };
        var index = -1;
        var responses = await RaceAsync(2, () => actions[Interlocked.Increment(ref index)]()).ConfigureAwait(true);

        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true), p => p == "/new");
    }

    [Fact]
    public async Task Renaming_one_page_is_not_refused_because_another_was_reordered()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-race-noconflict").ConfigureAwait(true);
        var ids = await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, """
            about
            services
            contact
            """).ConfigureAwait(true);

        for (var round = 0; round < 5; round++)
        {
            var contact = await PagesApi.NodeAsync(scenario.Admin, scenario.SiteA, "/contact").ConfigureAwait(true);
            var services = await PagesApi.NodeAsync(scenario.Admin, scenario.SiteA, "/services").ConfigureAwait(true);

            var actions = new[]
            {
                () => PagesApi.UpdateAsync(scenario.Admin, scenario.SiteA, ids["/contact"], $"Contact {round}", "contact", contact.GetProperty("version").GetInt32()),
                () => PagesApi.MoveAsync(scenario.Admin, scenario.SiteA, ids["/services"], null, round % 2 == 0 ? null : ids["/about"], services.GetProperty("version").GetInt32()),
            };
            var index = -1;
            var responses = await RaceAsync(2, () => actions[Interlocked.Increment(ref index)]()).ConfigureAwait(true);

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
    }

    private static async Task<HttpResponseMessage[]> RaceAsync(int count, Func<Task<HttpResponseMessage>> action)
    {
        using var barrier = new Barrier(count);
        var tasks = Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await action().ConfigureAwait(false);
        }));

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
