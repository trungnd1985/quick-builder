using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// The orphan sweep removes stored bytes that have no library entry once they are older than
/// the grace period, and never anything else (003 FR-012, SC-006, research M-15).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaOrphanSweepTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    private readonly ManualClock _clock = new();
    private MediaScenario _scenario = null!;
    private FaultyStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _scenario = await MediaScenario.CreateAsync(
            database,
            "orphan-sweep",
            services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.AddSingleton<IFileStorage>(sp => new FaultyStorage(sp.GetRequiredService<LocalFileStorage>()));
            }).ConfigureAwait(false);
        _storage = (FaultyStorage)_scenario.App.Services.GetRequiredService<IFileStorage>();
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private MediaOrphanSweepService Sweeper => _scenario.App.Services.GetRequiredService<MediaOrphanSweepService>();

    [Fact]
    public async Task Old_orphans_go_young_orphans_and_every_stored_file_stay()
    {
        // Every API call first: moving the clock later expires the administrator's session.
        var kept = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        // A failed insert whose best-effort delete also failed.
        _storage.FailDelete = new IOException("Simulated storage failure.");
        await CreateFailedInsertLeftoverAsync().ConfigureAwait(true);

        // A failed removal of the bytes after a permanent delete (021 TR-14).
        var purgeFailed = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.DeleteAsync(_scenario.Admin, _scenario.SiteB, purgeFailed.Id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _scenario.Admin.DeleteAsync(new Uri($"/api/v1/media/sites/{_scenario.SiteB}/trash/files/{purgeFailed.Id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        _storage.FailDelete = null;
        var leftovers = LeftoverFromFailedInsert();

        // Now move past the grace period, and age every stored file relative to the new time.
        _clock.Advance(Grace + TimeSpan.FromHours(1));
        var oldOrphanA = Orphan(_scenario.SiteA, age: Grace + TimeSpan.FromHours(2));
        var oldOrphanB = Orphan(_scenario.SiteB, age: Grace + TimeSpan.FromHours(5));
        var youngOrphan = Orphan(_scenario.SiteA, age: TimeSpan.FromHours(1));
        Age(_scenario.ObjectPath(_scenario.SiteA, kept.Id), TimeSpan.FromDays(30));
        Age(_scenario.ObjectPath(_scenario.SiteB, purgeFailed.Id), Grace + TimeSpan.FromHours(1));
        foreach (var leftover in leftovers)
        {
            Age(leftover, Grace + TimeSpan.FromHours(1));
        }

        var removed = await Sweeper.SweepAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(File.Exists(oldOrphanA));
        Assert.False(File.Exists(oldOrphanB));
        Assert.False(File.Exists(_scenario.ObjectPath(_scenario.SiteB, purgeFailed.Id)));
        Assert.Empty(LeftoverFromFailedInsert());
        Assert.True(File.Exists(youngOrphan), "Bytes younger than the grace period may belong to an upload in progress.");
        Assert.True(File.Exists(_scenario.ObjectPath(_scenario.SiteA, kept.Id)), "Bytes with a library entry are never touched.");
        Assert.Equal(4, removed);

        // The library is intact and still served.
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, kept.PublicPath).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Each_sites_removals_are_logged_by_site_without_names()
    {
        _clock.Advance(Grace + TimeSpan.FromHours(1));
        Orphan(_scenario.SiteA, age: Grace + TimeSpan.FromHours(2));
        Orphan(_scenario.SiteB, age: Grace + TimeSpan.FromHours(2));

        await Sweeper.SweepAsync(CancellationToken.None).ConfigureAwait(true);

        var sweeps = _scenario.App.Logs.Events
            .Where(e => e.MessageTemplate.Text.Contains("orphan", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Properties.ContainsKey("SiteId"))
            .Select(e => e.Properties["SiteId"].ToString().Trim('"'))
            .ToList();

        Assert.Contains(_scenario.SiteA.ToString(), sweeps);
        Assert.Contains(_scenario.SiteB.ToString(), sweeps);
    }

    [Fact]
    public async Task Concurrent_sweeps_both_succeed()
    {
        _clock.Advance(Grace + TimeSpan.FromHours(1));
        for (var i = 0; i < 20; i++)
        {
            Orphan(i % 2 == 0 ? _scenario.SiteA : _scenario.SiteB, age: Grace + TimeSpan.FromHours(2));
        }

        var sweeps = await Task.WhenAll(
            Sweeper.SweepAsync(CancellationToken.None),
            Sweeper.SweepAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Empty(_scenario.StoredObjects());
        Assert.InRange(sweeps.Sum(), 20, 40);
    }

    /// <summary>The sweep's cross-site read is deliberate and annotated (001 FR-013a).</summary>
    [Fact]
    public async Task The_sweep_reads_across_every_site_on_purpose()
    {
        var onA = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        var onB = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromDays(60));
        File.SetLastWriteTimeUtc(_scenario.ObjectPath(_scenario.SiteA, onA.Id), DateTime.UtcNow - TimeSpan.FromDays(60));
        File.SetLastWriteTimeUtc(_scenario.ObjectPath(_scenario.SiteB, onB.Id), DateTime.UtcNow - TimeSpan.FromDays(60));

        // With no ambient site, a site-filtered read would see no rows and call every object an
        // orphan. Seeing both sites' rows is what keeps both files.
        Assert.Equal(0, await Sweeper.SweepAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(2, _scenario.StoredObjects().Count);
    }

    /// <summary>013 MV-14: variants nobody can request any more go after an hour; the rest stay.</summary>
    [Fact]
    public async Task Variants_nobody_can_request_go_and_current_or_young_ones_stay()
    {
        var photo = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "photo.jpg", ImageFixtures.LargePhoto, "image/jpeg").ConfigureAwait(true);
        var withdrawn = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteB, "other.jpg", ImageFixtures.LargePhoto, "image/jpeg").ConfigureAwait(true);
        await using (var scope = _scenario.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MediaDbContext>().MediaFiles
                .AcrossAllSites("Test withdraws one file's variants.")
                .Where(f => f.Id == withdrawn.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.VariantsState, MediaVariantsState.Unavailable))
                .ConfigureAwait(true);
        }

        var old = TimeSpan.FromHours(2);
        var current = VariantFile(_scenario.SiteA, photo.Id, 1, 640, old);
        var staleVersion = VariantFile(_scenario.SiteA, photo.Id, 3, 640, old);
        var unconfigured = VariantFile(_scenario.SiteA, photo.Id, 1, 700, old);
        var deletedFile = VariantFile(_scenario.SiteA, Guid.NewGuid(), 1, 640, old);
        var unavailable = VariantFile(_scenario.SiteB, withdrawn.Id, 1, 320, old);
        var young = VariantFile(_scenario.SiteA, Guid.NewGuid(), 1, 640, TimeSpan.FromMinutes(10));
        Age(_scenario.ObjectPath(_scenario.SiteA, photo.Id), TimeSpan.FromDays(30));

        var removed = await Sweeper.SweepAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(current), "A variant still served is kept.");
        Assert.True(File.Exists(young), "A variant younger than an hour may be mid-production.");
        Assert.False(File.Exists(staleVersion));
        Assert.False(File.Exists(unconfigured));
        Assert.False(File.Exists(deletedFile));
        Assert.False(File.Exists(unavailable));
        Assert.True(File.Exists(_scenario.ObjectPath(_scenario.SiteA, photo.Id)), "Originals are never touched by the variant pass.");
        Assert.True(File.Exists(_scenario.ObjectPath(_scenario.SiteB, withdrawn.Id)));
        Assert.Equal(4, removed);
    }

    private string VariantFile(Guid siteId, Guid fileId, int version, int width, TimeSpan age)
    {
        var key = MediaStorageKeys.Variant(siteId, fileId, version, width).Value;
        var path = Path.Combine(_scenario.App.StoragePath, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SampleFiles.Png);
        Age(path, age);
        return path;
    }

    private void Age(string path, TimeSpan age) => File.SetLastWriteTimeUtc(path, (_clock.GetUtcNow() - age).UtcDateTime);

    /// <summary>Writes bytes with no library entry, aged as given.</summary>
    private string Orphan(Guid siteId, TimeSpan age)
    {
        var path = _scenario.ObjectPath(siteId, Guid.NewGuid());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SampleFiles.Png);
        Age(path, age);
        return path;
    }

    private async Task CreateFailedInsertLeftoverAsync()
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
#pragma warning disable EF1002 // Test-only DDL with no user input.
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE media.media_files ADD CONSTRAINT test_reject CHECK (false) NOT VALID").ConfigureAwait(false);
        try
        {
            var response = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Gif, "leftover.gif", "image/gif").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE media.media_files DROP CONSTRAINT test_reject").ConfigureAwait(false);
        }
#pragma warning restore EF1002

        Assert.Single(LeftoverFromFailedInsert());
    }

    /// <summary>Stored GIFs on site A: only the failed insert ever wrote one in these tests.</summary>
    private List<string> LeftoverFromFailedInsert() =>
        [.. _scenario.StoredObjects().Where(f => f.Contains(_scenario.SiteA.ToString("N"), StringComparison.Ordinal)
            && File.ReadAllBytes(f).AsSpan().StartsWith("GIF"u8))];
}
