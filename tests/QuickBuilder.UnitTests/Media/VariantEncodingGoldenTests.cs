using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NetVips;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.UnitTests.Media;

/// <summary>
/// What a variant is: upright, exactly the width asked for, no embedded metadata, alpha kept,
/// and no bigger than a recorded ceiling (013 FR-023, research MV-11); and which formats the
/// hardened libvips will open at all (MV-09).
/// </summary>
public sealed class VariantEncodingGoldenTests
{
    private static readonly ImagingRuntime Runtime = new(NullLogger<ImagingRuntime>.Instance);
    private static readonly VariantEncoder Encoder = new(Runtime);

    /// <summary>
    /// Ceilings in bytes per fixture and width, about 25 % above what the settings produced
    /// when they were set (measured 2026-10-01: 16,456 / 2,521 / 29,216 / 2,648 / 32,786).
    /// A setting that drifts (quality, effort, metadata kept) moves sizes past them.
    /// </summary>
    public static TheoryData<string, int, int> Ceilings => new()
    {
        { "jpeg", 320, 3_200 },
        { "jpeg", 960, 37_000 },
        { "png", 320, 3_400 },
        { "webp", 640, 41_000 },
        { "avif", 640, 21_000 },
    };

    private static (byte[] Bytes, MediaFileType Type) Fixture(string name) => name switch
    {
        "jpeg" => (ImageFixtures.LargePhoto, MediaFileType.Jpeg),
        "png" => (ImageFixtures.Logo, MediaFileType.Png),
        "webp" => (ImageFixtures.Webp, MediaFileType.WebP),
        _ => (ImageFixtures.Avif, MediaFileType.Avif),
    };

    [Theory]
    [MemberData(nameof(Ceilings))]
    public async Task Variants_are_the_requested_width_and_below_their_ceiling(string name, int width, int ceiling)
    {
        var (bytes, type) = Fixture(name);

        var variant = await Encoder.EncodeAsync(new MemoryStream(bytes), type, width, CancellationToken.None);

        using var image = Image.NewFromBuffer(variant);
        Assert.Equal(width, image.Width);
        Assert.True(variant.Length <= ceiling, $"{name} at {width}: {variant.Length} bytes, ceiling {ceiling}.");
    }

    [Fact]
    public async Task A_variant_is_upright_and_carries_no_embedded_metadata()
    {
        // Stored 1,200 × 800 with orientation 6, EXIF and a comment: displayed 800 × 1,200.
        using (var source = Image.NewFromBuffer(ImageFixtures.RotatedPhoto))
        {
            Assert.Contains("exif-data", source.GetFields());
        }

        var variant = await Encoder.EncodeAsync(new MemoryStream(ImageFixtures.RotatedPhoto), MediaFileType.Jpeg, 320, CancellationToken.None);

        using var image = Image.NewFromBuffer(variant);
        Assert.Equal(320, image.Width);
        Assert.Equal(480, image.Height);

        var fields = image.GetFields();
        Assert.DoesNotContain("exif-data", fields);
        Assert.DoesNotContain("xmp-data", fields);
        Assert.DoesNotContain("icc-profile-data", fields);
        Assert.DoesNotContain("QuickBuilder test fixture", Encoding.Latin1.GetString(variant), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transparency_is_kept()
    {
        var variant = await Encoder.EncodeAsync(new MemoryStream(ImageFixtures.Logo), MediaFileType.Png, 320, CancellationToken.None);

        using var image = Image.NewFromBuffer(variant);
        Assert.True(image.HasAlpha());
    }

    [Fact]
    public async Task A_cancelled_encoding_stops()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Encoder.EncodeAsync(new MemoryStream(ImageFixtures.LargePhoto), MediaFileType.Jpeg, 1920, cancelled.Token));
    }

    [Fact]
    public void Only_the_five_upload_formats_can_be_opened()
    {
        const string Svg = """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10"/></svg>""";

        // Static fields initialise on first access, not with the class: without this, a run of
        // this test alone would check a libvips nobody had restricted.
        Assert.True(Runtime.AvifVariants);

        Assert.Throws<VipsException>(() =>
        {
            using var svg = Image.NewFromBuffer(Encoding.UTF8.GetBytes(Svg));
            _ = svg.Avg();
        });

        foreach (var bytes in new[] { ImageFixtures.RotatedPhoto, ImageFixtures.Logo, ImageFixtures.AnimatedGif, ImageFixtures.Webp, ImageFixtures.Avif })
        {
            using var image = Image.NewFromBuffer(bytes);
            Assert.True(image.Width > 0);
        }
    }

    [Fact]
    public void The_shipped_libvips_writes_avif() => Assert.True(Runtime.AvifVariants);
}
