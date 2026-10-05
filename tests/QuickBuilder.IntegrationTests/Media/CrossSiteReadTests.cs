using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// 013's background reads that bypass the site filter each keep to the site they name
/// (constitution Principle V, tasks T103). The storage check's site-owned rows stay behind the
/// filter like any other.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CrossSiteReadTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "cross-site-reads").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_thumbnail_worker_produces_nothing_for_a_file_named_under_another_site()
    {
        var file = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "photo.jpg", ImageFixtures.RotatedPhoto, "image/jpeg");
        var worker = _scenario.App.Services.GetRequiredService<ThumbnailWorker>();
        var thumbnail = Path.Combine(_scenario.App.StoragePath, MediaStorageKeys.Variant(_scenario.SiteA, file.Id, 1, 320).Value.Replace('/', Path.DirectorySeparatorChar));
        var elsewhere = Path.Combine(_scenario.App.StoragePath, MediaStorageKeys.Variant(_scenario.SiteB, file.Id, 1, 320).Value.Replace('/', Path.DirectorySeparatorChar));

        await worker.ProduceAsync(new ThumbnailRequest(_scenario.SiteB, file.Id, 1), CancellationToken.None);
        Assert.False(File.Exists(thumbnail));
        Assert.False(File.Exists(elsewhere));

        await worker.ProduceAsync(new ThumbnailRequest(_scenario.SiteA, file.Id, 1), CancellationToken.None);
        Assert.True(File.Exists(thumbnail));
    }

    [Fact]
    public async Task The_search_text_backfill_works_and_logs_one_site_at_a_time()
    {
        var onA = (await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA)).Id;
        var onB = (await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB)).Id;
        await using (var scope = _scenario.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MediaDbContext>().MediaFiles
                .AcrossAllSites("Test sets rows back to provisional search text.")
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.SearchTextState, SearchTextState.Provisional));
        }

        var changed = await _scenario.App.Services.GetRequiredService<SearchTextBackfillService>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, changed);
        var rows = (await _scenario.RowsAsync()).Where(r => r.Id == onA || r.Id == onB).ToList();
        Assert.All(rows, r => Assert.Equal(SearchTextState.Normalised, r.SearchTextState));

        var logged = _scenario.App.Logs.Events
            .Where(e => e.MessageTemplate.Text.Contains("search-text backfill", StringComparison.Ordinal))
            .Select(e => (e.Properties["SiteId"].ToString().Trim('"'), e.Properties["Count"].ToString()))
            .ToList();
        Assert.Contains((_scenario.SiteA.ToString(), "1"), logged);
        Assert.Contains((_scenario.SiteB.ToString(), "1"), logged);
    }

    [Fact]
    public async Task A_storage_checks_missing_rows_of_one_site_never_appear_in_another_sites_filtered_read()
    {
        await using (var scope = _scenario.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            var check = new StorageCheck(Guid.CreateVersion7(), StorageCheckTrigger.Cli, "Local", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
            db.StorageChecks.Add(check);
            db.StorageCheckMissing.Add(new StorageCheckMissing(check.Id, _scenario.SiteA, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await MissingSeenByAsync(_scenario.SiteB));
        Assert.Single(await MissingSeenByAsync(_scenario.SiteA));
    }

    /// <summary>A read through the site filter, as a request on that site's host makes it.</summary>
    private async Task<List<StorageCheckMissing>> MissingSeenByAsync(Guid siteId)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<MediaDbContext>>();
        await using var db = new MediaDbContext(options, new FixedSite(siteId));
        return await db.StorageCheckMissing.AsNoTracking().ToListAsync();
    }

    private sealed class FixedSite(Guid siteId) : ISiteContext
    {
        public Guid? SiteId => siteId;

        public bool HasSite => true;
    }
}
