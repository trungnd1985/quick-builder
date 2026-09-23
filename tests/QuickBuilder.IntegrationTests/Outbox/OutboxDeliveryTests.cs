using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Outbox;

/// <summary>
/// User Story 3: work in one module triggers work in another without coupling them.
/// </summary>
/// <remarks>
/// Covers FR-015 through FR-019 and SC-005. These are the behaviours every later module
/// inherits, so they are worth proving against a real database rather than a substitute —
/// <c>FOR UPDATE SKIP LOCKED</c> and the processed-events unique constraint are the whole
/// mechanism, and neither exists outside PostgreSQL.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Outbox")]
public sealed class OutboxDeliveryTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("outbox").ConfigureAwait(false);

        // Background services are off, so each test drives one dispatch pass explicitly
        // and asserts on a pass it caused rather than racing a poll timer.
        _app = new QuickBuilderApp(_connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>FR-015: the event and the state change persist together.</summary>
    [Fact]
    public async Task Creating_a_site_stores_its_event_in_the_same_transaction()
    {
        await CreateSiteAsync("atomic.test").ConfigureAwait(true);

        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

        var sites = await context.Sites.IgnoreAutoIncludes().CountAsync().ConfigureAwait(true);
        var events = await context.OutboxMessages
            .Where(m => m.EventType == SiteCreated.EventType)
            .CountAsync()
            .ConfigureAwait(true);

        Assert.Equal(1, sites);
        Assert.Equal(1, events);
    }

    /// <summary>FR-016: a stored event is delivered when the dispatcher runs.</summary>
    [Fact]
    public async Task Dispatcher_delivers_a_pending_event()
    {
        await CreateSiteAsync("delivered.test").ConfigureAwait(true);

        var dispatched = await RunDispatchPassAsync().ConfigureAwait(true);

        Assert.Equal(1, dispatched);

        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var message = await context.OutboxMessages.SingleAsync().ConfigureAwait(true);

        Assert.NotNull(message.CompletedAtUtc);
        Assert.Null(message.FailedAtUtc);
        Assert.Equal(0, message.Attempts);
    }

    /// <summary>
    /// FR-017 and SC-005: redelivery produces the effect exactly once.
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole outbox design exists to support. At-least-once
    /// delivery is easy; making the effect land once is the part that is easy to get
    /// subtly wrong. Forcing the message back to pending simulates the redelivery a crash
    /// between "handler ran" and "message marked complete" would cause.
    /// </remarks>
    [Fact]
    public async Task Redelivering_an_event_does_not_repeat_the_effect()
    {
        await CreateSiteAsync("idempotent.test").ConfigureAwait(true);
        await RunDispatchPassAsync().ConfigureAwait(true);

        int markersAfterFirstPass;
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            markersAfterFirstPass = await context.ProcessedEvents.CountAsync().ConfigureAwait(true);

            // Force the message back to pending, as a crash before the completion commit
            // would leave it.
            await context.OutboxMessages
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.CompletedAtUtc, (DateTimeOffset?)null),
                    CancellationToken.None)
                .ConfigureAwait(true);
        }

        await RunDispatchPassAsync().ConfigureAwait(true);

        using var after = _app.Services.CreateScope();
        var afterContext = after.ServiceProvider.GetRequiredService<SitesDbContext>();

        var markersAfterSecondPass = await afterContext.ProcessedEvents.CountAsync().ConfigureAwait(true);
        var message = await afterContext.OutboxMessages.SingleAsync().ConfigureAwait(true);

        // The marker count is the proxy for "how many times the effect ran": the marker is
        // written in the same transaction as the effect, so one implies the other.
        Assert.Equal(1, markersAfterFirstPass);
        Assert.Equal(1, markersAfterSecondPass);

        // Re-completed without re-running, rather than left pending forever.
        Assert.NotNull(message.CompletedAtUtc);
    }

    /// <summary>FR-016: an undelivered event survives a restart of the application.</summary>
    [Fact]
    public async Task Undelivered_event_survives_an_application_restart()
    {
        await CreateSiteAsync("durable.test").ConfigureAwait(true);


        _app.Dispose();

        // A second host over the same database is what a restart looks like to the outbox.
        _app = new QuickBuilderApp(_connectionString);
        _ = _app.Services;

        var dispatched = await RunDispatchPassAsync().ConfigureAwait(true);

        Assert.Equal(1, dispatched);
    }

    private async Task CreateSiteAsync(string hostName)
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var response = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = hostName, hostNames = new[] { hostName } },
                CancellationToken.None)
            .ConfigureAwait(true);

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> RunDispatchPassAsync()
    {
        var dispatcher = _app.Services
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<OutboxDispatcher>()
            .FirstOrDefault()
            ?? ActivatorUtilities.CreateInstance<OutboxDispatcher>(_app.Services);

        var settings = _app.Services.GetRequiredService<IOutboxSettingsProvider>().Get();

        return await dispatcher
            .RunOnceAsync(settings with { Enabled = true }, CancellationToken.None)
            .ConfigureAwait(true);
    }
}

