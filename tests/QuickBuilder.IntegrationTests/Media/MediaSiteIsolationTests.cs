using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// SC-003: no media request ever returns a file of a site other than the one the host name
/// resolves to, and every miss looks the same (003 FR-014, US2 scenarios 2, 3 and 6).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaSiteIsolationTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;
    private FaultyStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _scenario = await MediaScenario.CreateAsync(
            database,
            "isolation",
            services => services.AddSingleton<IFileStorage>(sp => new FaultyStorage(sp.GetRequiredService<LocalFileStorage>()))).ConfigureAwait(false);
        _storage = (FaultyStorage)_scenario.App.Services.GetRequiredService<IFileStorage>();
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Every_host_serves_only_its_own_sites_files_and_every_miss_is_identical()
    {
        var onA = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        var onB = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true);
        var deleted = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        await DeleteRowAsync(deleted.Id).ConfigureAwait(true);

        // Own site: served.
        Assert.Equal(HttpStatusCode.OK, (await Public(MediaScenario.HostA, onA.PublicPath).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Public(MediaScenario.HostB, onB.PublicPath).ConfigureAwait(true)).StatusCode);

        // Every kind of miss, on both hosts, answers with the same bytes (apart from traceId).
        var reference = await BodyAsync(await Public(MediaScenario.HostA, MediaApi.PublicPath(Guid.NewGuid(), "png")).ConfigureAwait(true)).ConfigureAwait(true);
        var misses = new (string Host, string Path, string Case)[]
        {
            (MediaScenario.HostB, onA.PublicPath, "site A's file on site B's host"),
            (MediaScenario.HostA, onB.PublicPath, "site B's file on site A's host"),
            (MediaScenario.HostA, MediaApi.PublicPath(onA.Id, "gif"), "right id, wrong extension"),
            (MediaScenario.HostA, deleted.PublicPath, "a deleted file"),
            (MediaScenario.HostB, MediaApi.PublicPath(Guid.NewGuid(), "pdf"), "an id never issued, on B"),
            (MediaScenario.HostA, $"/_media/{onA.Id:N}", "no extension"),
            (MediaScenario.HostA, $"/_media/{onA.Id:N}.PNG", "upper-case extension"),
            (MediaScenario.HostA, $"/_media/{onA.Id.ToString("N").ToUpperInvariant()}.png", "upper-case id"),
            (MediaScenario.HostA, $"/_media/{onA.Id:N}0.png", "overlong id"),
            (MediaScenario.HostA, $"/_media/{onA.Id:D}.png", "hyphenated id"),
            (MediaScenario.HostA, "/_media/..%2F..%2Fetc%2Fpasswd", "encoded traversal"),
            (MediaScenario.HostA, "/_media/%5C..%5Cwindows%5Cwin.ini", "encoded backslash traversal"),
        };

        foreach (var (host, path, description) in misses)
        {
            var response = await Public(host, path).ConfigureAwait(true);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{description}: expected 404, got {(int)response.StatusCode}");
            Assert.True(reference == await BodyAsync(response).ConfigureAwait(true), $"{description}: the 404 body differs from a never-issued id's");
        }
    }

    [Fact]
    public async Task A_host_that_matches_no_site_gets_the_existing_not_resolved_answer()
    {
        var onA = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var response = await Public("unknown.test", onA.PublicPath).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("sites.not_resolved", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    /// <summary>US2 scenario 6: a path trying to leave the media space never reaches storage.</summary>
    [Theory]
    [InlineData("/_media/..%2F..%2Fetc%2Fpasswd")]
    [InlineData("/_media/%2e%2e%2f%2e%2e%2fapp.dll")]
    [InlineData("/_media/..%5C..%5Cwindows")]
    [InlineData("/_media//etc/passwd")]
    [InlineData("/_media/")]
    [InlineData("/_media")]
    [InlineData("/_media/0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a%2Fmedia.png")]
    public async Task Traversal_attempts_never_open_anything_in_storage(string path)
    {
        var opensBefore = _storage.Opens;

        var response = await Public(MediaScenario.HostA, path).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(opensBefore, _storage.Opens);
    }

    private Task<HttpResponseMessage> Public(string host, string path) => MediaApi.PublicAsync(_scenario.App, host, path);

    private static Task<string> BodyAsync(HttpResponseMessage response) => SignInHelpers.BodyWithoutTraceIdAsync(response);

    private async Task DeleteRowAsync(Guid fileId)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        await context.MediaFiles
            .AcrossAllSites("Test: remove a row directly to model a deleted file.")
            .Where(f => f.Id == fileId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
