using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Images uploaded before 013 get their dimensions after the upgrade, one site at a time,
/// pages are told, and thumbnails are queued (013 FR-027, research MV-10; analysis C1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ImageMetadataBackfillTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "metadata-backfill").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private ImageMetadataBackfillService Backfill => _scenario.App.Services.GetRequiredService<ImageMetadataBackfillService>();

    [Fact]
    public async Task Pending_images_on_every_site_are_measured_marked_or_left_alone()
    {
        var photo = await AsPendingAsync(_scenario.SiteA, "photo.jpg", ImageFixtures.LargePhoto, "image/jpeg").ConfigureAwait(true);
        var huge = await AsPendingAsync(_scenario.SiteB, "huge.png", ImageFixtures.HugePixels, "image/png").ConfigureAwait(true);
        var cut = await AsPendingAsync(_scenario.SiteB, "cut.jpg", ImageFixtures.TruncatedJpeg, "image/jpeg").ConfigureAwait(true);

        // Two passes at once, as two instances would run them.
        await Task.WhenAll(Backfill.RunOnceAsync(CancellationToken.None), Backfill.RunOnceAsync(CancellationToken.None)).ConfigureAwait(true);

        var rows = (await _scenario.RowsAsync().ConfigureAwait(true)).ToDictionary(r => r.Id);
        Assert.Equal(MediaMetadataState.Recorded, rows[photo].MetadataState);
        Assert.Equal((6_000, 4_000), (rows[photo].Width, rows[photo].Height));
        Assert.Equal(MediaMetadataState.OverLimit, rows[huge].MetadataState);
        Assert.Equal(MediaMetadataState.Unreadable, rows[cut].MetadataState);

        // Only the measured image tells pages, exactly once.
        Assert.Equal(1, await DimensionsEventsAsync().ConfigureAwait(true));

        // Its thumbnail is queued.
        var queue = _scenario.App.Services.GetRequiredService<ThumbnailQueue>();
        Assert.True(queue.Reader.TryRead(out var request));
        Assert.Equal(photo, request.FileId);

        // Each site's batches are logged under that site (Principle V).
        var logged = _scenario.App.Logs.Events
            .Where(e => e.MessageTemplate.Text.Contains("metadata recorded", StringComparison.Ordinal))
            .Select(e => e.Properties["SiteId"].ToString().Trim('"'))
            .ToHashSet();
        Assert.Contains(_scenario.SiteA.ToString(), logged);
        Assert.Contains(_scenario.SiteB.ToString(), logged);

        Assert.Equal(0, await Backfill.RunOnceAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>Uploads, then sets the row back to how a pre-013 file looks after the migration.</summary>
    private async Task<Guid> AsPendingAsync(Guid siteId, string name, byte[] bytes, string type)
    {
        Guid id;
        if (type == "image/png" && bytes.Length == ImageFixtures.HugePixels.Length)
        {
            // Over the limit, so it cannot be uploaded today: store its bytes and row directly.
            id = Guid.NewGuid();
            var key = MediaStorageKeys.For(siteId, id);
            await using var scope = _scenario.App.Services.CreateAsyncScope();
            var storage = scope.ServiceProvider.GetRequiredService<QuickBuilder.Shared.Infrastructure.Storage.IFileStorage>();
            await storage.WriteAsync(key, new MemoryStream(bytes)).ConfigureAwait(false);
            var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            db.MediaFiles.Add(MediaFile.Create(id, siteId, name, MediaFileType.Png, "png", bytes.Length, DateTimeOffset.UtcNow, Guid.NewGuid(), key.Value));
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        else
        {
            id = (await LibraryApi.UploadNamedAsync(_scenario.Admin, siteId, name, bytes, type).ConfigureAwait(false)).Id;
        }

        await using var reset = _scenario.App.Services.CreateAsyncScope();
        await reset.ServiceProvider.GetRequiredService<MediaDbContext>().MediaFiles
            .AcrossAllSites("Test sets a row back to pre-013 state.")
            .Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.MetadataState, MediaMetadataState.Pending)
                .SetProperty(f => f.Width, (int?)null)
                .SetProperty(f => f.Height, (int?)null))
            .ConfigureAwait(false);

        // Thumbnails queued by the uploads are not this test's business.
        var queue = _scenario.App.Services.GetRequiredService<ThumbnailQueue>();
        while (queue.Reader.TryRead(out _))
        {
        }

        return id;
    }

    private async Task<int> DimensionsEventsAsync()
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<MediaDbContext>().Set<OutboxMessage>()
            .Where(m => m.EventType == FileChanged.EventType)
            .Select(m => m.Payload)
            .ToListAsync().ConfigureAwait(false);
        return messages.Count(p => p.Contains("\"dimensions\"", StringComparison.Ordinal));
    }
}
