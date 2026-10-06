using System.Diagnostics;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// FR-022, FR-028 (013 clarification): an image's thumbnail is produced right after upload,
/// within 5 seconds, without holding up the upload's answer (research MV-13).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ThumbnailLatencyTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly TimeSpan Target = TimeSpan.FromSeconds(5);

    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(
            database,
            "thumbnail-latency",
            ControllableEncoder.Install,
            runBackgroundServices: true).ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_thumbnail_exists_within_five_seconds_and_the_upload_did_not_wait_for_it()
    {
        // Slow enough that an upload waiting for it would show.
        ControllableEncoder.Of(_scenario.App.Services).Delay = TimeSpan.FromSeconds(1);

        var watch = Stopwatch.StartNew();
        var file = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "big.jpg", ImageFixtures.TwentyMebibytePhoto, "image/jpeg").ConfigureAwait(true);
        var uploaded = watch.Elapsed;

        var key = MediaStorageKeys.Variant(_scenario.SiteA, file.Id, 1, 320).Value.Replace('/', Path.DirectorySeparatorChar);
        var thumbnail = Path.Combine(_scenario.App.StoragePath, key);

        // The answer came before the thumbnail could exist.
        Assert.False(File.Exists(thumbnail), $"The upload waited for the thumbnail ({uploaded}).");

        while (!File.Exists(thumbnail) && watch.Elapsed - uploaded < Target)
        {
            await Task.Delay(100).ConfigureAwait(true);
        }

        Assert.True(File.Exists(thumbnail), $"No thumbnail {Target} after the upload completed.");
        Assert.Equal(1, ControllableEncoder.Of(_scenario.App.Services).ByWidth.GetValueOrDefault(320));
    }
}
