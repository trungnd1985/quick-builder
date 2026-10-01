using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Header-only dimensions, orientation, animation and the pixel limit (013 research MV-10).</summary>
public sealed class ImageMetadataTests
{
    private const long Limit = 100_000_000;

    static ImageMetadataTests() => _ = new ImagingRuntime(NullLogger<ImagingRuntime>.Instance);

    private static ProbedImage Read(byte[] bytes) => ImageMetadataReader.Read(new MemoryStream(bytes), Limit);

    [Fact]
    public void Orientation_five_to_eight_swaps_the_displayed_dimensions()
    {
        var probed = Read(ImageFixtures.RotatedPhoto);

        Assert.Equal(MediaMetadataState.Recorded, probed.State);
        Assert.Equal((800, 1_200), (probed.Metadata!.Value.Width, probed.Metadata.Value.Height));
        Assert.False(probed.Metadata.Value.IsAnimated);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("webp")]
    public void Animated_images_are_detected_with_their_page_size(string format)
    {
        var probed = Read(format == "gif" ? ImageFixtures.AnimatedGif : ImageFixtures.AnimatedWebp);

        Assert.True(probed.Metadata!.Value.IsAnimated);
        Assert.Equal((400, 300), (probed.Metadata.Value.Width, probed.Metadata.Value.Height));
    }

    [Fact]
    public void An_image_over_the_limit_is_reported_from_its_header_alone()
    {
        var bytes = ImageFixtures.HugePixels;
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();

        var probed = Read(bytes);

        watch.Stop();
        Assert.Equal(MediaMetadataState.OverLimit, probed.State);
        Assert.Null(probed.Metadata);

        // 144 megapixels would be hundreds of megabytes decoded; reading a header is neither
        // slow nor large. (libvips allocates natively, so time is the stronger signal.)
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Took {watch.Elapsed}.");
        Assert.True(GC.GetTotalAllocatedBytes(precise: true) - before < 50_000_000);
    }

    [Fact]
    public void A_truncated_header_is_unreadable_rather_than_thrown() =>
        Assert.Equal(MediaMetadataState.Unreadable, Read(ImageFixtures.TruncatedJpeg).State);

    [Fact]
    public void Avif_and_still_webp_are_read() =>
        Assert.All(
            [ImageFixtures.Avif, ImageFixtures.Webp],
            bytes => Assert.Equal((1_600, 1_000), (Read(bytes).Metadata!.Value.Width, Read(bytes).Metadata!.Value.Height)));
}
