using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>The orphan sweep never takes the bytes of a file in the trash (021 TR-18).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaOrphanSweepTrashTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_trashed_files_bytes_survive_a_sweep_long_past_the_grace_period()
    {
        var clock = new ManualClock();
        using var scenario = await MediaScenario.CreateAsync(database, "sweep-trash", services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))).ConfigureAwait(true);
        var file = await MediaApi.UploadOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.DeleteAsync(scenario.Admin, scenario.SiteA, file.Id).ConfigureAwait(true)).StatusCode);

        var path = scenario.ObjectPath(scenario.SiteA, file.Id);
        clock.Advance(TimeSpan.FromDays(365));
        File.SetLastWriteTimeUtc(path, clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(300));

        await scenario.App.Services.GetRequiredService<MediaOrphanSweepService>().SweepAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(path), "The bytes stay until the file is purged.");
    }
}
