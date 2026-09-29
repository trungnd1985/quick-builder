using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Outbox;

/// <summary>
/// A message stored for later is not claimed before its time (012 research PR-14).
/// </summary>
/// <remarks>
/// This is the whole mechanism of scheduled publishing: the schedule's due message waits in
/// the outbox with <c>available_at_utc</c> set to the due time. Time is moved by editing that
/// column, never by waiting.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Outbox")]
public sealed class DelayedMessageTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("delayed").ConfigureAwait(false);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_future_message_waits_until_its_time()
    {
        var future = DateTimeOffset.UtcNow.AddHours(1);
        var integrationEvent = new SiteDisabled(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), ActorUserId: null);

        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            var message = OutboxMessage.From(integrationEvent, future);

            Assert.Equal(future, message.AvailableAtUtc);

            context.OutboxMessages.Add(message);
            await context.SaveChangesAsync().ConfigureAwait(true);
        }

        Assert.Equal(0, await RunDispatchPassAsync().ConfigureAwait(true));

        // Time passes: the due time is now in the past.
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            await context.OutboxMessages
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.AvailableAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)))
                .ConfigureAwait(true);
        }

        Assert.Equal(1, await RunDispatchPassAsync().ConfigureAwait(true));
    }

    [Fact]
    public void Without_a_time_a_message_is_available_when_its_event_occurred()
    {
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-3);
        var message = OutboxMessage.From(new SiteEnabled(Guid.NewGuid(), occurred, Guid.NewGuid(), ActorUserId: null));

        Assert.Equal(occurred, message.AvailableAtUtc);
        Assert.Equal(SiteEnabled.EventType, message.EventType);
    }

    private async Task<int> RunDispatchPassAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_app.Services);
        var settings = _app.Services.GetRequiredService<IOutboxSettingsProvider>().Get();

        return await dispatcher
            .RunOnceAsync(settings with { Enabled = true }, CancellationToken.None)
            .ConfigureAwait(true);
    }
}
