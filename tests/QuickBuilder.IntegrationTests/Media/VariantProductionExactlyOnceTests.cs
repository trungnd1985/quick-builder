using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// SC-005: 100 simultaneous first requests for one variant, split across two instances, produce
/// it exactly once, and every request gets the same bytes (013 FR-022, research MV-12).
/// </summary>
/// <remarks>
/// Two application instances over one database and one storage directory, as two containers
/// sharing a bucket would be. A slowed encoder widens the window in which the requests overlap.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class VariantProductionExactlyOnceTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Host = "variants-once.test";

    private readonly string _storage = QuickBuilderApp.CreateTemporaryStorage();
    private QuickBuilderApp _first = null!;
    private QuickBuilderApp _second = null!;

    public async Task InitializeAsync()
    {
        var connection = await database.CreateDatabaseAsync("variants-once").ConfigureAwait(false);
        _first = new QuickBuilderApp(connection, configureTestServices: ControllableEncoder.Install, storagePath: _storage);
        _second = new QuickBuilderApp(connection, configureTestServices: ControllableEncoder.Install, storagePath: _storage);
        _ = _first.Services;
        _ = _second.Services;
    }

    public Task DisposeAsync()
    {
        _first.Dispose();
        _second.Dispose();
        QuickBuilderApp.TryDeleteDirectory(_storage);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task One_hundred_first_requests_over_two_instances_produce_the_variant_once()
    {
        var admin = await _first.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await MediaApi.CreateSiteAsync(admin, "Shared", Host).ConfigureAwait(true);
        var file = await LibraryApi.UploadNamedAsync(admin, siteId, "photo.jpg", ImageFixtures.LargePhoto, "image/jpeg").ConfigureAwait(true);

        ControllableEncoder.Of(_first.Services).Delay = TimeSpan.FromMilliseconds(500);
        ControllableEncoder.Of(_second.Services).Delay = TimeSpan.FromMilliseconds(500);

        var path = $"/_media/{file.Id:N}-v1-w960.jpg";
        var requests = Enumerable.Range(0, 100)
            .Select(i => MediaApi.PublicAsync(i % 2 == 0 ? _first : _second, Host, path))
            .ToList();
        var responses = await Task.WhenAll(requests).ConfigureAwait(true);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsByteArrayAsync())).ConfigureAwait(true);
        Assert.Single(bodies.Select(Convert.ToBase64String).Distinct());

        var produced = ControllableEncoder.Of(_first.Services).ByWidth.GetValueOrDefault(960)
            + ControllableEncoder.Of(_second.Services).ByWidth.GetValueOrDefault(960);
        Assert.Equal(1, produced);
    }

    [Fact]
    public async Task A_claim_left_by_a_dead_instance_is_taken_over()
    {
        var admin = await _first.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await MediaApi.CreateSiteAsync(admin, "Crashed", "crashed.test").ConfigureAwait(true);
        var file = await LibraryApi.UploadNamedAsync(admin, siteId, "photo.jpg", ImageFixtures.LargePhoto, "image/jpeg").ConfigureAwait(true);

        // A claim from long ago: its instance died mid-production.
        await using (var scope = _first.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            db.VariantClaims.Add(new VariantClaim(siteId, file.Id, 1, 640, DateTimeOffset.UtcNow.AddHours(-1), Guid.NewGuid()));
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        var response = await MediaApi.PublicAsync(_second, "crashed.test", $"/_media/{file.Id:N}-v1-w640.jpg").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        await using var check = _first.Services.CreateAsyncScope();
        var claims = await check.ServiceProvider.GetRequiredService<MediaDbContext>().VariantClaims
            .AcrossAllSites("Test inspection.")
            .CountAsync().ConfigureAwait(true);
        Assert.Equal(0, claims);
    }
}
