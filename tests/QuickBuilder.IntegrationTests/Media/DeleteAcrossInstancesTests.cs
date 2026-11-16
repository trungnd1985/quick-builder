using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// SC-007: a deleted file's address answers not found on the very next request, on every
/// instance (003 FR-004, research M-14 — nothing caches file metadata).
/// </summary>
/// <remarks>
/// Two application instances over one database and one storage directory: what two
/// containers behind a load balancer look like, with the media volume shared between them.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DeleteAcrossInstancesTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly string _storage = QuickBuilderApp.CreateTemporaryStorage();
    private QuickBuilderApp _first = null!;
    private QuickBuilderApp _second = null!;

    public async Task InitializeAsync()
    {
        var connection = await database.CreateDatabaseAsync("delete-instances").ConfigureAwait(false);
        _first = new QuickBuilderApp(connection, storagePath: _storage);
        _second = new QuickBuilderApp(connection, storagePath: _storage);

        // Start both, so the second's startup work is not mistaken for anything later.
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
    public async Task A_delete_on_one_instance_is_not_found_on_the_other_at_the_very_next_request()
    {
        var admin = await _first.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await MediaApi.CreateSiteAsync(admin, "Shared", "shared.test").ConfigureAwait(true);
        var file = await MediaApi.UploadOkAsync(admin, siteId).ConfigureAwait(true);

        // Served by the other instance, and warm there.
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_second, "shared.test", file.PublicPath).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_second, "shared.test", file.PublicPath).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await MediaApi.DeleteAsync(admin, siteId, file.Id).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_second, "shared.test", file.PublicPath).ConfigureAwait(true)).StatusCode);
    }
}
