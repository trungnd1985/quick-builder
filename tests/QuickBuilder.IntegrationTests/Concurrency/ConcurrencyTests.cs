using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Migrations;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.IntegrationTests.Concurrency;

/// <summary>
/// Proves the multi-instance guarantees under genuine contention (FR-019, FR-024,
/// FR-040a, SC-012).
/// </summary>
/// <remarks>
/// Per the clarification recorded in spec.md: the contention being promised is a
/// database-level one, so several migrators and several dispatchers run concurrently
/// against one real database inside this test process. The pipeline does not orchestrate
/// multiple application containers — that would test Docker, not the locking.
/// <para>
/// These are the tests that matter most in this feature. Both failures they guard against
/// are silent: a second instance re-applying migrations corrupts a schema without an
/// error, and two dispatchers claiming one event produces a duplicate side effect that
/// nothing reports. Neither shows up in single-instance testing.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Concurrency")]
public sealed class ConcurrencyTests(DatabaseFixture database, ITestOutputHelper output) : IAsyncLifetime
{
    private const int ParallelInstances = 6;

    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("concurrency").ConfigureAwait(true);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// FR-024 and SC-012: several instances starting at once migrate exactly once.
    /// </summary>
    /// <remarks>
    /// Every host is built and started simultaneously, which is what a scaled deployment
    /// does on a rolling restart. The advisory lock is the only thing serialising them —
    /// without it, concurrent <c>MigrateAsync</c> calls race on the history table and can
    /// apply the same migration twice.
    /// </remarks>
    [Fact]
    public async Task Concurrent_migrators_apply_each_migration_exactly_once()
    {
        var apps = Enumerable
            .Range(0, ParallelInstances)
            .Select(_ => new QuickBuilderApp(_connectionString))
            .ToList();

        try
        {
            // Building the host is what runs startup migration, so kicking these off
            // together is the contention. Task.Run because host build is synchronous.
            var starts = apps.Select(app => Task.Run(() => app.Services)).ToArray();
            await Task.WhenAll(starts).ConfigureAwait(true);

            // Every instance must agree the schema is up to date, and the history table
            // must contain each migration once — not once per instance.
            using var scope = apps[0].Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

            var applied = (await context.Database.GetAppliedMigrationsAsync().ConfigureAwait(true)).ToList();
            var pending = (await context.Database.GetPendingMigrationsAsync().ConfigureAwait(true)).ToList();

            output.WriteLine($"{ParallelInstances} instances; applied: {string.Join(", ", applied)}");

            Assert.NotEmpty(applied);
            Assert.Empty(pending);
            Assert.Equal(applied.Count, applied.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            foreach (var app in apps)
            {
                app.Dispose();
            }
        }
    }

    /// <summary>
    /// FR-019 and SC-012: concurrent dispatchers claim each event exactly once.
    /// </summary>
    /// <remarks>
    /// The assertion is on the processed-events markers rather than on what each pass
    /// returned. A marker is written in the same transaction as the handler's effect, so
    /// "one marker per event" is the same statement as "the effect happened once" — which
    /// is the guarantee callers actually depend on.
    /// <para>
    /// If <c>SKIP LOCKED</c> were missing, the passes would either deadlock or double
    /// deliver; both show up here as a marker count that is not equal to the event count.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Concurrent_dispatchers_claim_each_event_exactly_once()
    {
        using var app = new QuickBuilderApp(_connectionString);
        _ = app.Services;

        const int eventCount = 24;
        await SeedPendingEventsAsync(app, eventCount).ConfigureAwait(true);

        var settings = app.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with
        {
            Enabled = true,
            BatchSize = 5, // Smaller than the backlog, so every dispatcher keeps contending.
        };

        // All passes start together and keep going until the backlog is drained.
        var passes = Enumerable
            .Range(0, ParallelInstances)
            .Select(_ => Task.Run(async () =>
            {
                var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(app.Services);
                var claimed = 0;

                for (var round = 0; round < 12; round++)
                {
                    claimed += await dispatcher
                        .RunOnceAsync(settings, CancellationToken.None)
                        .ConfigureAwait(true);
                }

                return claimed;
            }))
            .ToArray();

        var claimedPerPass = await Task.WhenAll(passes).ConfigureAwait(true);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

        var markers = await context.ProcessedEvents.CountAsync().ConfigureAwait(true);
        var completed = await context.OutboxMessages
            .CountAsync(m => m.CompletedAtUtc != null)
            .ConfigureAwait(true);
        var failed = await context.OutboxMessages
            .CountAsync(m => m.FailedAtUtc != null)
            .ConfigureAwait(true);

        output.WriteLine(
            $"claimed per pass: [{string.Join(", ", claimedPerPass)}]; " +
            $"markers: {markers}; completed: {completed}; failed: {failed}");

        // The heart of it: one effect per event, no matter how many dispatchers raced.
        Assert.Equal(eventCount, markers);
        Assert.Equal(eventCount, completed);
        Assert.Equal(0, failed);
    }

    /// <summary>
    /// An event claimed by an instance that dies becomes eligible again (FR-019).
    /// </summary>
    /// <remarks>
    /// Simulated by claiming inside a transaction that is then rolled back, which is
    /// exactly what PostgreSQL does for a connection that drops. The design rests on this:
    /// D-02 chose row locks over a lease column precisely because the server reclaims them
    /// with no sweeper of our own. If that were untrue, a crash would strand events
    /// permanently and nothing would say so.
    /// </remarks>
    [Fact]
    public async Task Event_claimed_by_a_lost_instance_becomes_eligible_again()
    {
        using var app = new QuickBuilderApp(_connectionString);
        _ = app.Services;

        await SeedPendingEventsAsync(app, 1).ConfigureAwait(true);

        var provider = app.Services.GetServices<global::QuickBuilder.Shared.Infrastructure.Modules.IModuleDatabaseProvider>().First();

        using (var scope = app.Services.CreateScope())
        {
            await using var context = provider.CreateContext(scope.ServiceProvider);
            await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(true);

            var claimed = await context.Set<OutboxMessage>()
                .FromSqlRaw(
                    "SELECT * FROM \"sites\".outbox_messages " +
                    "WHERE completed_at_utc IS NULL AND failed_at_utc IS NULL " +
                    "ORDER BY available_at_utc LIMIT 1 FOR UPDATE SKIP LOCKED")
                .ToListAsync()
                .ConfigureAwait(true);

            Assert.Single(claimed);

            // The instance dies here: no commit, so the server releases the lock.
            await transaction.RollbackAsync().ConfigureAwait(true);
        }

        var settings = app.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(app.Services);

        var dispatched = await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, dispatched);
    }

    /// <summary>Writes pending outbox rows directly, bypassing HTTP for speed.</summary>
    private static async Task SeedPendingEventsAsync(QuickBuilderApp app, int count)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            var siteId = Guid.CreateVersion7();

            context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                EventType = global::QuickBuilder.Sites.Contracts.Events.SiteCreated.EventType,
                Payload = System.Text.Json.JsonSerializer.Serialize(
                    new global::QuickBuilder.Sites.Contracts.Events.SiteCreated(
                        Guid.CreateVersion7(), now, siteId, $"Seeded {i}", [$"seed-{i}.test"])),
                OccurredAtUtc = now,
                AvailableAtUtc = now,
            });
        }

        await context.SaveChangesAsync().ConfigureAwait(true);
    }
}
