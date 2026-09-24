using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Identity retention (spec FR-029a, SC-012): old history leaves, recent history stays
/// exactly as written, and nothing that is still in use is touched.
/// </summary>
/// <remarks>
/// Rows are written in SQL so each can be placed at an exact point in time; the sweep
/// then runs with the clock moved 366 days on.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class IdentityRetentionTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly ManualClock _clock = new();
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("retention").ConfigureAwait(false);
        _app = new QuickBuilderApp(
            connectionString,
            configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock)));
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sweep_removes_only_what_has_outlived_its_purpose()
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services).ConfigureAwait(true);
        var start = _clock.GetUtcNow();
        var afterYear = start + TimeSpan.FromDays(366);

        var oldEvent = Guid.NewGuid();
        var recentEvent = Guid.NewGuid();

        await ExecuteAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO identity.security_events (id, occurred_at_utc, event_type, outcome, subject_user_id, details)
            VALUES ({oldEvent}, {start}, 'sign_in', 'failed', {user.Id}, CAST({FailedDetails} AS jsonb)),
                   ({recentEvent}, {start + TimeSpan.FromDays(2)}, 'sign_in', 'succeeded', {user.Id}, CAST({EmptyDetails} AS jsonb));

            INSERT INTO identity.sign_in_throttles (kind, key, consecutive_failures, updated_at_utc)
            VALUES ('client', 'stale', 3, {start}),
                   ('client', 'fresh', 1, {afterYear - TimeSpan.FromHours(1)});

            INSERT INTO identity.sessions (id, user_id, token_hash, created_at_utc, last_seen_at_utc, absolute_expires_at_utc)
            VALUES (gen_random_uuid(), {user.Id}, '\x01', {start}, {start}, {start + TimeSpan.FromHours(12)}),
                   (gen_random_uuid(), {user.Id}, '\x02', {afterYear}, {afterYear}, {afterYear + TimeSpan.FromHours(12)});

            INSERT INTO identity.set_password_links (id, user_id, token_hash, issued_at_utc, expires_at_utc, invalidated_at_utc, invalidation_reason)
            VALUES (gen_random_uuid(), {user.Id}, '\x03', {start}, {start + TimeSpan.FromDays(1)}, {start + TimeSpan.FromHours(1)}, 'replaced');
            INSERT INTO identity.set_password_links (id, user_id, token_hash, issued_at_utc, expires_at_utc)
            VALUES (gen_random_uuid(), {user.Id}, '\x04', {afterYear}, {afterYear + TimeSpan.FromDays(1)});

            INSERT INTO identity.known_devices (id, user_id, token_hash, created_at_utc, last_used_at_utc, consecutive_failures, revoked_at_utc)
            VALUES (gen_random_uuid(), {user.Id}, '\x05', {afterYear}, {afterYear}, 0, {afterYear}),
                   (gen_random_uuid(), {user.Id}, '\x06', {start}, {start}, 0, NULL),
                   (gen_random_uuid(), {user.Id}, '\x07', {afterYear}, {afterYear}, 0, NULL);
            """)).ConfigureAwait(true);

        var recentBefore = await RowAsync(recentEvent).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromDays(366));

        var sweep = await Sweeper.SweepAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, sweep.SecurityEvents);
        Assert.Equal(1, sweep.Throttles);
        Assert.Equal(1, sweep.Sessions);
        Assert.Equal(1, sweep.SetPasswordLinks);
        Assert.Equal(2, sweep.KnownDevices);

        Assert.Null(await RowAsync(oldEvent).ConfigureAwait(true));
        Assert.Equal(recentBefore, await RowAsync(recentEvent).ConfigureAwait(true));

        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal("fresh", Assert.Single(await context.SignInThrottles.Select(t => t.Key).ToListAsync().ConfigureAwait(true)));
        Assert.Equal(1, await context.Sessions.CountAsync().ConfigureAwait(true));
        Assert.Equal(1, await context.SetPasswordLinks.CountAsync().ConfigureAwait(true));
        Assert.Equal(1, await context.KnownDevices.CountAsync().ConfigureAwait(true));
    }

    /// <summary>More than one batch of history goes, and two sweeps at once do not collide.</summary>
    [Fact]
    public async Task Concurrent_sweeps_clear_more_than_one_batch()
    {
        var old = _clock.GetUtcNow();
        await ExecuteAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO identity.security_events (id, occurred_at_utc, event_type, outcome, details)
            SELECT gen_random_uuid(), {old}, 'sign_in', 'failed', CAST({EmptyDetails} AS jsonb)
            FROM generate_series(1, {IdentityRetentionService.BatchSize + 50});
            """)).ConfigureAwait(true);

        _clock.Advance(TimeSpan.FromDays(366));

        var sweeps = await Task.WhenAll(
            Sweeper.SweepAsync(CancellationToken.None),
            Sweeper.SweepAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(IdentityRetentionService.BatchSize + 50, sweeps.Sum(s => s.SecurityEvents));

        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SecurityEvents.CountAsync().ConfigureAwait(true));
    }

    private const string FailedDetails = """{"reason":"wrong_password"}""";
    private const string EmptyDetails = "{}";

    private IdentityRetentionService Sweeper => _app.Services.GetRequiredService<IdentityRetentionService>();

    private async Task ExecuteAsync(Func<IdentityDbContext, Task<int>> statement)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        await statement(scope.ServiceProvider.GetRequiredService<IdentityDbContext>()).ConfigureAwait(false);
    }

    /// <summary>The whole stored row as text, to prove a kept event is unchanged.</summary>
    private async Task<string?> RowAsync(Guid id)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database
            .SqlQuery<string>($"SELECT row_to_json(e)::text AS \"Value\" FROM identity.security_events e WHERE e.id = {id}")
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);
    }
}
