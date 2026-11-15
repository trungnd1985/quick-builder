using System.Net;
using System.Net.Http.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Storage;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// US3 scenarios 3 and 5 (013 FR-036): two instances on one database and one bucket share
/// media, and an unreachable bucket takes them out of rotation until it returns.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaAcrossInstancesTests(DatabaseFixture database, S3Fixture s3) : IAsyncLifetime, IClassFixture<S3Fixture>
{
    private string _connection = null!;
    private MediaScenario _first = null!;
    private QuickBuilderApp _second = null!;
    private HttpClient _secondAdmin = null!;

    public async Task InitializeAsync()
    {
        _connection = await database.CreateDatabaseAsync("across-instances").ConfigureAwait(false);
        var storage = s3.Configuration();

        _first = await MediaScenario.CreateAsync(database, "across-instances", connectionString: _connection, settings: storage).ConfigureAwait(false);
        _second = new QuickBuilderApp(_connection, settings: storage);
        var operator2 = await IdentitySeed.SeedUserAsync(_second.Services, administrator: true, displayName: "Second").ConfigureAwait(false);
        _secondAdmin = await IdentitySeed.CreateSignedInClientAsync(_second, operator2.Id).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _second.Dispose();
        _first.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_file_uploaded_through_one_instance_is_listed_previewed_served_resized_and_deleted_through_the_other()
    {
        var file = await LibraryApi.UploadNamedAsync(_first.Admin, _first.SiteA, "photo.jpg", ImageFixtures.LargePhoto, "image/jpeg");

        var listed = await LibraryApi.ListOkAsync(_secondAdmin, _first.SiteA);
        Assert.Contains(listed.Items, f => f.Id == file.Id);

        var preview = await MediaApi.PreviewAsync(_secondAdmin, _first.SiteA, file.Id);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(ImageFixtures.LargePhoto.Length, (await preview.Content.ReadAsByteArrayAsync()).Length);

        var original = await MediaApi.PublicAsync(_second, MediaScenario.HostA, file.PublicPath);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);

        // Produced and stored by the second instance, then read from the bucket by the first.
        var variant = $"/_media/{file.Id:N}-v1-w640.jpg";
        var produced = await MediaApi.PublicAsync(_second, MediaScenario.HostA, variant);
        Assert.Equal(HttpStatusCode.OK, produced.StatusCode);
        Assert.Contains("immutable", produced.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var fromBucket = await MediaApi.PublicAsync(_first.App, MediaScenario.HostA, variant);
        Assert.Equal(await produced.Content.ReadAsByteArrayAsync(), await fromBucket.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, (await MediaApi.DeleteAsync(_secondAdmin, _first.SiteA, file.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_first.App, MediaScenario.HostA, file.PublicPath)).StatusCode);
        Assert.DoesNotContain((await LibraryApi.ListOkAsync(_first.Admin, _first.SiteA)).Items, f => f.Id == file.Id);
    }

    [Fact]
    public async Task A_file_replaced_through_one_instance_is_served_new_through_the_other()
    {
        var file = await LibraryApi.UploadNamedAsync(_first.Admin, _first.SiteA, "logo.png", ImageFixtures.Logo, "image/png");

        var response = await ReplaceFileApiTests.ReplaceAsync(_secondAdmin, _first.SiteA, file.Id, 1, "logo-v2.png", ImageFixtures.LogoV2);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replaced = (await response.Content.ReadFromJsonAsync<QuickBuilder.Media.Contracts.MediaFileDto>(MediaApi.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_first.App, MediaScenario.HostA, file.PublicPath)).StatusCode);
        var served = await MediaApi.PublicAsync(_first.App, MediaScenario.HostA, replaced.PublicPath);
        Assert.Equal(ImageFixtures.LogoV2, await served.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// US3 scenario 5, on a bucket of its own: pausing the shared one would stall other
    /// classes running alongside.
    /// </summary>
    [Fact]
    public async Task An_unreachable_bucket_makes_instances_unready_and_uploads_refused_until_it_returns()
    {
        var container = await S3Fixture.StartDedicatedAsync();
        try
        {
            using (var setup = S3Fixture.ClientFor(S3Fixture.UrlOf(container)))
            {
                await setup.PutBucketAsync("own");
            }

            var settings = S3Fixture.ConfigurationFor(S3Fixture.UrlOf(container), "own");

            using var app = new QuickBuilderApp(_connection, settings: settings);
            var admin = await IdentitySeed.SeedUserAsync(app.Services, administrator: true, displayName: "Own");
            using var client = await IdentitySeed.CreateSignedInClientAsync(app, admin.Id);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);

            await container.PauseAsync();
            try
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);
            }
            finally
            {
                await container.UnpauseAsync();
            }

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);
            await MediaApi.UploadOkAsync(client, _first.SiteA);

            // Gone entirely: refused promptly as unavailable, with nothing recorded.
            var before = (await _first.RowsAsync()).Count;
            await container.StopAsync();
            var refused = await MediaApi.UploadAsync(client, _first.SiteA, SampleFiles.Png, "after.png", "image/png");
            await LibraryApi.RefusedAsync(refused, HttpStatusCode.ServiceUnavailable, "media.storage_unavailable");
            Assert.Equal(before, (await _first.RowsAsync()).Count);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }
}
