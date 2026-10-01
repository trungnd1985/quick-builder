using System.Net;
using System.Net.Http.Headers;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// What an image upload records and refuses (013 FR-027, research MV-10): displayed dimensions
/// at upload, the pixel limit from the header alone, and unreadable headers kept as uploaded.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ImageUploadTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "image-upload").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_image_is_recorded_with_its_displayed_dimensions()
    {
        var file = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "rotated.jpg", ImageFixtures.RotatedPhoto, "image/jpeg").ConfigureAwait(true);

        Assert.Equal("recorded", file.MetadataState);
        Assert.Equal((800, 1_200), (file.Width, file.Height));
    }

    [Fact]
    public async Task An_image_over_the_pixel_limit_is_refused_and_nothing_is_stored()
    {
        var before = _scenario.StoredObjects().Count;

        using var content = new ByteArrayContent(ImageFixtures.HugePixels);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, content, "huge.png", "image/png").ConfigureAwait(true);

        await LibraryApi.RefusedAsync(response, HttpStatusCode.BadRequest, "media.dimensions_too_large").ConfigureAwait(true);
        Assert.Equal(before, _scenario.StoredObjects().Count);
        Assert.Empty(await _scenario.RowsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task An_unreadable_header_is_kept_and_served_as_uploaded()
    {
        var file = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "cut.jpg", ImageFixtures.TruncatedJpeg, "image/jpeg").ConfigureAwait(true);

        Assert.Equal("unreadable", file.MetadataState);
        Assert.Null(file.Width);
        Assert.Empty(file.Variants);

        var served = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true);
        Assert.Equal(ImageFixtures.TruncatedJpeg, await served.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }
}
