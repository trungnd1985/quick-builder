using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// The hourly sweep (022 research PV-19): links that ended more than 30 days ago and failure
/// windows older than an hour go; everything else stays, also when two sweeps run at once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkSweepTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly ManualClock _clock = new();
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() =>
        _world = await TemplatesWorld.CreateAsync(database, "preview-sweep", s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock)))
            .ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private async Task<List<Guid>> LinkIdsAsync()
    {
        await using var scope = _world.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EditorDbContext>().PreviewLinks
            .AcrossAllSites("Test: every link, whatever its site.")
            .Select(l => l.Id)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task<int> WindowsAsync()
    {
        await using var scope = _world.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EditorDbContext>().PreviewLinkFailures.CountAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Links_ended_more_than_thirty_days_ago_and_old_windows_go_and_the_rest_stay()
    {
        var page = await _world.PageAsync("about").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("About")).ConfigureAwait(true);

        var expiresSoon = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, _clock.GetUtcNow().AddHours(1)).ConfigureAwait(true);
        var revokedNow = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, _clock.GetUtcNow().AddDays(30)).ConfigureAwait(true);
        await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, revokedNow.Id).ConfigureAwait(true);
        var recent = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, _clock.GetUtcNow().AddDays(30)).ConfigureAwait(true);
        await PreviewLinksApi.OpenAsync(_world.App, "/_preview/l/" + new string('z', 43), CmsScenario.HostA).ConfigureAwait(true);

        // 31 days on: the first two ended over 30 days ago; the third expired only a day ago.
        _clock.Advance(TimeSpan.FromDays(31));
        var sweep = _world.App.Services.GetRequiredService<PreviewLinkSweepService>();
        await Task.WhenAll(sweep.SweepAsync(CancellationToken.None), sweep.SweepAsync(CancellationToken.None)).ConfigureAwait(true);

        var kept = await LinkIdsAsync().ConfigureAwait(true);
        Assert.DoesNotContain(expiresSoon.Id, kept);
        Assert.DoesNotContain(revokedNow.Id, kept);
        Assert.Contains(recent.Id, kept);
        Assert.Equal(0, await WindowsAsync().ConfigureAwait(true));
    }
}
