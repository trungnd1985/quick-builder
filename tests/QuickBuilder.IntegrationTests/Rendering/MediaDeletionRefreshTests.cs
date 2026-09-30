using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Events;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// User Story 6: deleting a media file refreshes the live pages that showed it, directly or
/// through a component, once however often the event arrives (012 data-model §3.2).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaDeletionRefreshTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _file;
    private Guid _direct;
    private Guid _throughComponent;
    private Guid _unrelated;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "media-refresh").ConfigureAwait(false);
        _file = (await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(false)).Id;

        var component = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Banner").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var componentPath = PagesApi.ComponentPath(_scenario.SiteA, component);
        await PublishingApi.SaveAsync(_scenario.Admin, componentPath, ImageDocument(_file)).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, componentPath).ConfigureAwait(false);

        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "direct\nthrough\nunrelated").ConfigureAwait(false);
        _direct = pages["/direct"];
        _throughComponent = pages["/through"];
        _unrelated = pages["/unrelated"];

        await PublishAsync(_direct, ImageDocument(_file)).ConfigureAwait(false);
        await PublishAsync(_throughComponent, PublishingApi.HeadingDocument("Through", component)).ConfigureAwait(false);
        await PublishAsync(_unrelated, PublishingApi.HeadingDocument("Unrelated")).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Pages_showing_a_deleted_file_are_refreshed_and_stop_showing_it()
    {
        var image = $"/_media/{_file:N}.png";
        Assert.Contains(image, await VisitAsync("/direct").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains(image, await VisitAsync("/through").ConfigureAwait(true), StringComparison.Ordinal);
        var before = await GenerationsAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, _file).ConfigureAwait(true)).StatusCode);
        await DispatchAsync().ConfigureAwait(true);

        var after = await GenerationsAsync().ConfigureAwait(true);
        Assert.Equal(before[_direct] + 1, after[_direct]);
        Assert.Equal(before[_throughComponent] + 1, after[_throughComponent]);
        Assert.Equal(before[_unrelated], after[_unrelated]);

        Assert.DoesNotContain(image, await VisitAsync("/direct").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.DoesNotContain(image, await VisitAsync("/through").ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_duplicate_event_refreshes_once()
    {
        var before = await GenerationsAsync().ConfigureAwait(true);
        var deleted = new FileDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, _scenario.SiteA, _file, null);

        for (var i = 0; i < 2; i++)
        {
            await using var scope = _scenario.App.Services.CreateAsyncScope();
            foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<FileDeleted>>())
            {
                await handler.HandleAsync(deleted, CancellationToken.None).ConfigureAwait(true);
            }
        }

        var after = await GenerationsAsync().ConfigureAwait(true);
        Assert.Equal(before[_direct] + 1, after[_direct]);
        Assert.Equal(before[_throughComponent] + 1, after[_throughComponent]);
    }

    [Fact]
    public async Task The_delete_writes_its_event_with_the_delete()
    {
        await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, _file).ConfigureAwait(true);

        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<QuickBuilder.Media.Infrastructure.MediaDbContext>();
        var message = await media.Set<OutboxMessage>().SingleAsync(m => m.EventType == FileDeleted.EventType).ConfigureAwait(true);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(_file, payload.RootElement.GetProperty("FileId").GetGuid());
    }

    private async Task PublishAsync(Guid pageId, string document)
    {
        var path = PagesApi.PagePath(_scenario.SiteA, pageId);
        await PublishingApi.SaveAsync(_scenario.Admin, path, document).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, long>> GenerationsAsync() =>
        await _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<GenerationRow>("SELECT subject_id AS \"Id\", generation AS \"Generation\" FROM pages.publications WHERE site_id = {0} AND subject_kind = 'page'", _scenario.SiteA)
            .ToDictionaryAsync(r => r.Id, r => r.Generation)).ConfigureAwait(false);

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    private Task<string> VisitAsync(string address) =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri(address, UriKind.Relative));

    /// <summary>A document with one Section holding an Image of <paramref name="file"/>.</summary>
    private static string ImageDocument(Guid file) =>
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":[{"id":"i1","type":"image","props":{"media":{"mediaId":"""
        + JsonSerializer.Serialize(file.ToString())
        + """},"alt":"A picture"},"children":[]}]}]}}""";

    private sealed record GenerationRow(Guid Id, long Generation);
}
