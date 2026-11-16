using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Searching a trash of 10,000 entries stays under one second at the 95th percentile (021 SC-003).
/// Opt-in, like <c>TransferPerformanceTests</c>: set <c>QB_TRASH_PERF=1</c> on the reference setup.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrashListPerformanceTests(DatabaseFixture database)
{
    private const int Entries = 10_000;
    private const int Samples = 40;

    [SkippableFact]
    public async Task Searching_and_filtering_ten_thousand_entries_stays_under_a_second()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("QB_TRASH_PERF") == "1", "Manual: set QB_TRASH_PERF=1 on the reference setup.");

        using var scenario = await PagesScenario.CreateAsync(database, "trash-perf");
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        await PagesApi.SeedTreeAsync(admin, site, "home*");

        await using (var scope = scenario.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pages.pages (id, site_id, parent_id, position, title, slug, path, depth, is_home, version, created_at_utc, created_by, updated_at_utc, updated_by,
                                         trashed_at_utc, trashed_by, trashed_position, trashed_path, trashed_was_home, trashed_had_schedule)
                SELECT gen_random_uuid(), {site}, NULL, 0, 'Pricing page ' || n, 'p-' || n, '/p-' || n, 1, false, 1, now(), {Guid.NewGuid()}, now(), {Guid.NewGuid()},
                       now() - (n || ' minutes')::interval, {Guid.NewGuid()}, 0, '/p-' || n, false, false
                FROM generate_series(1, {Entries}) AS n
                """);
        }

        var root = $"/api/v1/pages/sites/{site}/trash/pages";
        string[] queries = [string.Empty, "?q=pricing", "?q=9999", $"?trashedBy={Guid.NewGuid()}", $"?from={DateTimeOffset.UtcNow.AddDays(-1):O}"];

        var timings = new List<double>();
        for (var i = 0; i < Samples; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await admin.GetAsync(new Uri(root + queries[i % queries.Length].Replace("+", "%2B", StringComparison.Ordinal), UriKind.Relative));
            watch.Stop();
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(Samples * 0.95) - 1];
        Assert.True(p95 < 1000, $"p95 was {p95:F0} ms.");
    }
}
