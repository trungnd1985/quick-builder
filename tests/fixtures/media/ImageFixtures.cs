using NetVips;

namespace QuickBuilder.TestFixtures.Media;

/// <summary>
/// The images 013's tests need, generated deterministically at test time (tasks T045).
/// </summary>
/// <remarks>
/// Generated rather than committed: a 20 MiB JPEG and a 144-megapixel PNG do not belong in the
/// repository, and generated images say exactly what they are. Each is built once per process
/// and cached. Linked into the unit and integration test projects.
/// </remarks>
internal static class ImageFixtures
{
    private static readonly Lazy<byte[]> RotatedPhotoBytes = new(() => Photo(1_200, 800, orientation: 6, quality: 90));
    private static readonly Lazy<byte[]> LargePhotoBytes = new(() => Photo(6_000, 4_000, orientation: 1, quality: 85));
    private static readonly Lazy<byte[]> TwentyMebibytePhotoBytes = new(MakeTwentyMebibytePhoto);
    private static readonly Lazy<byte[]> LogoBytes = new(() => MakeLogo(800, 400, 0));
    private static readonly Lazy<byte[]> LogoV2Bytes = new(() => MakeLogo(900, 300, 120));
    private static readonly Lazy<byte[]> AnimatedGifBytes = new(() => Animated("gif"));
    private static readonly Lazy<byte[]> AnimatedWebpBytes = new(() => Animated("webp"));
    private static readonly Lazy<byte[]> HugePixelsBytes = new(() => Image.Black(12_000, 12_000).Cast(Enums.BandFormat.Uchar).PngsaveBuffer(compression: 9));
    private static readonly Lazy<byte[]> AvifBytes = new(() => PhotoImage(1_600, 1_000).HeifsaveBuffer(q: 60, compression: Enums.ForeignHeifCompression.Av1, effort: 2));
    private static readonly Lazy<byte[]> WebpBytes = new(() => PhotoImage(1_600, 1_000).WebpsaveBuffer(q: 80));
    private static readonly Lazy<byte[]> LargeAvifBytes = new(MakeLargeAvif);

    /// <summary>1,200 × 800 stored, EXIF orientation 6 (displayed 800 × 1,200), with EXIF and XMP.</summary>
    public static byte[] RotatedPhoto => RotatedPhotoBytes.Value;

    /// <summary>A 6,000 × 4,000 photo-like JPEG, orientation 1.</summary>
    public static byte[] LargePhoto => LargePhotoBytes.Value;

    /// <summary>A JPEG just under the default 20 MiB upload limit.</summary>
    public static byte[] TwentyMebibytePhoto => TwentyMebibytePhotoBytes.Value;

    /// <summary>An 800 × 400 PNG with transparency.</summary>
    public static byte[] Logo => LogoBytes.Value;

    /// <summary>A 900 × 300 PNG with transparency, a different logo.</summary>
    public static byte[] LogoV2 => LogoV2Bytes.Value;

    /// <summary>A three-frame animated GIF, 400 × 300.</summary>
    public static byte[] AnimatedGif => AnimatedGifBytes.Value;

    /// <summary>A three-frame animated WebP, 400 × 300.</summary>
    public static byte[] AnimatedWebp => AnimatedWebpBytes.Value;

    /// <summary>A 12,000 × 12,000 (144-megapixel) black PNG: tiny on disk, over the default pixel limit.</summary>
    public static byte[] HugePixels => HugePixelsBytes.Value;

    /// <summary>A 1,600 × 1,000 AVIF.</summary>
    public static byte[] Avif => AvifBytes.Value;

    /// <summary>A 6,000 × 4,000 AVIF as large as fits under the default 20 MiB upload limit (T070).</summary>
    public static byte[] LargeAvif => LargeAvifBytes.Value;

    /// <summary>A 1,600 × 1,000 still WebP.</summary>
    public static byte[] Webp => WebpBytes.Value;

    /// <summary>A JPEG header cut off after its first bytes: detected as JPEG, unreadable by libvips.</summary>
    public static byte[] TruncatedJpeg => RotatedPhoto.AsSpan(0, 64).ToArray();

    private static byte[] Photo(int width, int height, int orientation, int quality)
    {
        using var image = PhotoImage(width, height);
        using var tagged = image.Mutate(m =>
        {
            m.Set(GValue.GIntType, "orientation", orientation);
            m.Set(GValue.GStrType, "exif-ifd0-Artist", "QuickBuilder test fixture");
        });
        return tagged.JpegsaveBuffer(q: quality, keep: Enums.ForeignKeep.All);
    }

    /// <summary>Noise plus gradients: compresses like a photograph, not like a flat colour.</summary>
    private static Image PhotoImage(int width, int height)
    {
        using var noise = Image.Gaussnoise(width, height, sigma: 40, mean: 128);
        using var ramp = Image.Xyz(width, height)[0] * (255.0 / width);
        return noise.Bandjoin(ramp, noise * 0.5 + 60).Cast(Enums.BandFormat.Uchar).Copy(interpretation: Enums.Interpretation.Srgb);
    }

    private static byte[] MakeTwentyMebibytePhoto()
    {
        // The largest noise JPEG under the limit, found by lowering quality from the top.
        const long limit = 20L * 1024 * 1024;
        using var image = PhotoImage(6_000, 4_000);
        for (var quality = 95; quality >= 50; quality -= 5)
        {
            var bytes = image.JpegsaveBuffer(q: quality);
            if (bytes.Length <= limit)
            {
                return bytes;
            }
        }

        throw new InvalidOperationException("Could not make a JPEG under 20 MiB.");
    }

    private static byte[] MakeLargeAvif()
    {
        const long limit = 20L * 1024 * 1024;
        using var image = PhotoImage(6_000, 4_000);
        for (var quality = 90; quality >= 30; quality -= 20)
        {
            var bytes = image.HeifsaveBuffer(q: quality, compression: Enums.ForeignHeifCompression.Av1, effort: 0);
            if (bytes.Length <= limit)
            {
                return bytes;
            }
        }

        throw new InvalidOperationException("Could not make an AVIF under 20 MiB.");
    }

    private static byte[] MakeLogo(int width, int height, int hue)
    {
        using var colour = Image.Black(width, height, bands: 3) + new double[] { hue, 60, 200 - (hue / 2) };
        using var alpha = (Image.Xyz(width, height)[0] * (255.0 / width)).Cast(Enums.BandFormat.Uchar);
        using var rgba = colour.Cast(Enums.BandFormat.Uchar).Bandjoin(alpha).Copy(interpretation: Enums.Interpretation.Srgb);
        return rgba.PngsaveBuffer();
    }

    private static byte[] Animated(string format)
    {
        const int width = 400;
        const int pageHeight = 300;
        var frames = Enumerable.Range(0, 3)
            .Select(i => (Image.Black(width, pageHeight, bands: 3) + new double[] { 80 * i, 40, 120 }).Cast(Enums.BandFormat.Uchar))
            .ToArray();

        using var strip = Image.Arrayjoin(frames, across: 1);
        foreach (var frame in frames)
        {
            frame.Dispose();
        }

        using var srgb = strip.Copy(interpretation: Enums.Interpretation.Srgb);
        using var animated = srgb.Mutate(m =>
        {
            m.Set(GValue.GIntType, "page-height", pageHeight);
            m.Set(GValue.ArrayIntType, "delay", new[] { 100, 100, 100 });
            m.Set(GValue.GIntType, "loop", 0);
        });

        return format == "gif" ? animated.GifsaveBuffer() : animated.WebpsaveBuffer(q: 80);
    }
}
