using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Security events are evidence: they can be added, never rewritten or removed through the
/// context (002 research R-14).
/// </summary>
/// <remarks>
/// The entity has no public setters, so the realistic ways to tamper are marking an entry
/// modified or removing it. Both must fail at SaveChanges, whatever code path tries.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SecurityEventAppendOnlyTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("events").ConfigureAwait(false));
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_event_can_be_added()
    {
        var id = await AddEventAsync().ConfigureAwait(true);

        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.True(await context.SecurityEvents.AnyAsync(e => e.Id == id).ConfigureAwait(true));
    }

    [Fact]
    public async Task Modifying_a_stored_event_is_refused()
    {
        var id = await AddEventAsync().ConfigureAwait(true);

        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var stored = await context.SecurityEvents.SingleAsync(e => e.Id == id).ConfigureAwait(true);

        context.Entry(stored).Property(e => e.Outcome).CurrentValue = SecurityEventOutcomes.Failed;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync()).ConfigureAwait(true);
    }

    [Fact]
    public async Task Deleting_a_stored_event_is_refused()
    {
        var id = await AddEventAsync().ConfigureAwait(true);

        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var stored = await context.SecurityEvents.SingleAsync(e => e.Id == id).ConfigureAwait(true);

        context.SecurityEvents.Remove(stored);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync()).ConfigureAwait(true);

        await using var verifyScope = _app.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await verify.SecurityEvents.AnyAsync(e => e.Id == id).ConfigureAwait(true));
    }

    private async Task<Guid> AddEventAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var securityEvent = SecurityEvent.Create(
            SecurityEventTypes.SignOut,
            SecurityEventOutcomes.Succeeded,
            DateTimeOffset.UtcNow,
            actorUserId: null,
            subjectUserId: null,
            clientAddress: null);

        context.SecurityEvents.Add(securityEvent);
        await context.SaveChangesAsync().ConfigureAwait(true);
        return securityEvent.Id;
    }
}
