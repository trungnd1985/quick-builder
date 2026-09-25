using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Features.RecordSiteSecurityEvents;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Sites;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// 009 FR-028, research S-11: host-name changes and disabling or enabling a site are
/// recorded in the security event record, exactly once each, even when redelivered.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SiteSecurityEventTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;
    private Guid _adminId;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("site-security-events").ConfigureAwait(false));
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
        var admin = await IdentitySeed.SeedUserAsync(_app.Services, administrator: true).ConfigureAwait(false);
        _adminId = admin.Id;
        _admin = await IdentitySeed.CreateSignedInClientAsync(_app, admin.Id).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Host_name_changes_are_recorded_once_with_actor_site_and_host_name()
    {
        var siteId = (await SitesApi.CreateOkAsync(_admin, "Audited", ["audited.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();
        await SitesApi.AddHostNameAsync(_admin, siteId, "www.audited.test").ConfigureAwait(true);
        await SitesApi.RemoveHostNameAsync(_admin, siteId, "www.audited.test").ConfigureAwait(true);

        await DispatchAsync().ConfigureAwait(true);

        foreach (var type in new[] { SecurityEventTypes.SiteHostNameAdded, SecurityEventTypes.SiteHostNameRemoved })
        {
            var recorded = Assert.Single(await EventsAsync(type).ConfigureAwait(true));
            Assert.Equal(_adminId, recorded.ActorUserId);
            Assert.Equal(SecurityEventOutcomes.Succeeded, recorded.Outcome);

            using var details = JsonDocument.Parse(recorded.Details);
            Assert.Equal(siteId, details.RootElement.GetProperty("siteId").GetGuid());
            Assert.Equal("www.audited.test", details.RootElement.GetProperty("hostName").GetString());
        }
    }

    [Fact]
    public async Task Disabling_and_enabling_are_recorded_once_each_and_a_no_op_records_nothing()
    {
        var siteId = (await SitesApi.CreateOkAsync(_admin, "Toggled", ["toggled.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();

        await SitesApi.DisableAsync(_admin, siteId).ConfigureAwait(true);
        await SitesApi.DisableAsync(_admin, siteId).ConfigureAwait(true);
        await SitesApi.EnableAsync(_admin, siteId).ConfigureAwait(true);
        await SitesApi.EnableAsync(_admin, siteId).ConfigureAwait(true);

        await DispatchAsync().ConfigureAwait(true);

        var disabled = Assert.Single(await EventsAsync(SecurityEventTypes.SiteDisabled).ConfigureAwait(true));
        var enabled = Assert.Single(await EventsAsync(SecurityEventTypes.SiteEnabled).ConfigureAwait(true));
        Assert.Equal(_adminId, disabled.ActorUserId);
        Assert.Equal(_adminId, enabled.ActorUserId);
    }

    /// <summary>
    /// The handler deduplicates in Identity's own processed-events table, because the
    /// dispatcher's marker lives in the Sites schema, in another transaction.
    /// </summary>
    [Fact]
    public async Task Redelivering_an_event_after_it_was_recorded_adds_nothing()
    {
        var siteId = (await SitesApi.CreateOkAsync(_admin, "Redelivered", ["redelivered.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();
        await SitesApi.DisableAsync(_admin, siteId).ConfigureAwait(true);
        await DispatchAsync().ConfigureAwait(true);

        var delivered = await OutboxEventAsync<SiteDisabled>(siteId).ConfigureAwait(true);
        for (var i = 0; i < 3; i++)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RecordSiteSecurityEventsHandler>()
                .HandleAsync(delivered, CancellationToken.None)
                .ConfigureAwait(true);
        }

        Assert.Single(
            await EventsAsync(SecurityEventTypes.SiteDisabled).ConfigureAwait(true),
            e => e.Details.Contains(siteId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_app.Services);
        var settings = _app.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    private async Task<List<SecurityEvent>> EventsAsync(string eventType)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .SecurityEvents.AsNoTracking()
            .Where(e => e.EventType == eventType)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task<TEvent> OutboxEventAsync<TEvent>(Guid siteId)
        where TEvent : Shared.Kernel.Events.IIntegrationEvent
    {
        var eventType = TEvent.EventType;
        await using var scope = _app.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<SitesDbContext>()
            .OutboxMessages.AsNoTracking()
            .Where(m => m.EventType == eventType)
            .ToListAsync()
            .ConfigureAwait(false);

        return messages
            .Select(m => JsonSerializer.Deserialize<TEvent>(m.Payload)!)
            .Single(e => JsonSerializer.Serialize(e).Contains(siteId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
