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

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// Pages show the site's default sharing image and logo without referencing them, so a change
/// to either file refreshes every live page of that site (017 research SE-20).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SiteImageRefreshTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _defaultImage;
    private Guid _pageImage;
    private Guid _plain;
    private Guid _withImage;
    private Guid _otherSite;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-site-images").ConfigureAwait(false);
        _defaultImage = (await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "default.png").ConfigureAwait(false)).Id;
        _pageImage = (await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "page.png").ConfigureAwait(false)).Id;
        await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, defaultImageId: _defaultImage).ConfigureAwait(false);

        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "plain\nwith-image").ConfigureAwait(false);
        _plain = pages["/plain"];
        _withImage = pages["/with-image"];
        await PublishAsync(_scenario.SiteA, _plain, PublishingApi.HeadingDocument("Plain")).ConfigureAwait(false);
        await PublishAsync(_scenario.SiteA, _withImage, SeoApi.DocumentWithSeo("With", $$"""{ "ogImage": { "mediaId": "{{_pageImage}}" } }""")).ConfigureAwait(false);

        var other = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteB, "elsewhere").ConfigureAwait(false);
        _otherSite = other["/elsewhere"];
        await PublishAsync(_scenario.SiteB, _otherSite, PublishingApi.HeadingDocument("Elsewhere")).ConfigureAwait(false);
        await DispatchAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Deleting_the_default_image_refreshes_every_live_page_of_the_site_only()
    {
        var before = await GenerationsAsync().ConfigureAwait(true);

        var deleted = await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, _defaultImage).ConfigureAwait(true);
        Assert.True(deleted.IsSuccessStatusCode, $"delete answered {(int)deleted.StatusCode}");
        await DispatchAsync().ConfigureAwait(true);

        var after = await GenerationsAsync().ConfigureAwait(true);
        Assert.True(after[_plain] > before[_plain]);
        Assert.True(after[_withImage] > before[_withImage]);
        Assert.Equal(before[_otherSite], after[_otherSite]);
    }

    [Fact]
    public async Task Deleting_a_page_s_own_sharing_image_refreshes_that_page()
    {
        var before = await GenerationsAsync().ConfigureAwait(true);

        // A live page shows it, so the delete is confirmed (013 FR-015).
        var deleted = await UsageScenario.DeleteAsync(_scenario.Admin, _scenario.SiteA, _pageImage, confirmInUse: true).ConfigureAwait(true);
        Assert.True(deleted.IsSuccessStatusCode, $"delete answered {(int)deleted.StatusCode}");
        await DispatchAsync().ConfigureAwait(true);

        var after = await GenerationsAsync().ConfigureAwait(true);
        Assert.True(after[_withImage] > before[_withImage]);
        Assert.Equal(before[_plain], after[_plain]);
    }

    [Fact]
    public async Task A_changed_default_image_refreshes_the_site_once_per_event()
    {
        var before = await GenerationsAsync().ConfigureAwait(true);
        var changed = new FileChanged(Guid.CreateVersion7(), DateTimeOffset.UtcNow, _scenario.SiteA, _defaultImage, FileChangeReasons.Replaced);

        for (var i = 0; i < 2; i++)
        {
            await using var scope = _scenario.App.Services.CreateAsyncScope();
            foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<FileChanged>>())
            {
                await handler.HandleAsync(changed, CancellationToken.None).ConfigureAwait(true);
            }
        }

        var after = await GenerationsAsync().ConfigureAwait(true);
        Assert.Equal(before[_plain] + 1, after[_plain]);
        Assert.Equal(before[_withImage] + 1, after[_withImage]);
        Assert.Equal(before[_otherSite], after[_otherSite]);
    }

    private async Task PublishAsync(Guid siteId, Guid pageId, string document)
    {
        var path = PagesApi.PagePath(siteId, pageId);
        await PublishingApi.SaveAsync(_scenario.Admin, path, document).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, path).ConfigureAwait(false);
    }

    private Task<Dictionary<Guid, long>> GenerationsAsync() =>
        _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<GenerationRow>("SELECT subject_id AS \"Id\", generation AS \"Generation\" FROM pages.publications WHERE subject_kind = 'page'")
            .ToDictionaryAsync(r => r.Id, r => r.Generation));

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    private sealed record GenerationRow(Guid Id, long Generation);
}
