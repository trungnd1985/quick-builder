using NetVips;
using QuickBuilder.Media.Domain;

namespace QuickBuilder.Media.Imaging;

/// <summary>Produces one resized copy of an image (013 research MV-11).</summary>
internal interface IVariantEncoder
{
    /// <summary>
    /// <paramref name="original"/> resized to <paramref name="width"/> pixels wide, in the
    /// original's format. Throws <see cref="OperationCanceledException"/> when cancelled and
    /// <see cref="VariantEncodingException"/> when the image cannot be encoded.
    /// </summary>
    Task<byte[]> EncodeAsync(Stream original, MediaFileType type, int width, CancellationToken cancellationToken);
}

/// <summary>An image that libvips could not turn into a variant.</summary>
internal sealed class VariantEncodingException : Exception
{
    public VariantEncodingException()
    {
    }

    public VariantEncodingException(string message) : base(message)
    {
    }

    public VariantEncodingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The variant pipeline over libvips (013 research MV-11).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><c>thumbnail</c> shrinks on load where the format allows (JPEG decodes at 1/2, 1/4
/// or 1/8 size directly), and never enlarges.</item>
/// <item>It turns the image upright from its EXIF orientation (FR-023).</item>
/// <item>An embedded ICC profile is honoured and the result converted to sRGB, so colours
/// survive dropping the profile.</item>
/// <item>The result is saved in the original's format with <c>keep: none</c>: no EXIF, GPS,
/// XMP or ICC data (FR-023). Alpha is kept, because nothing flattens it.</item>
/// </list>
/// The quality settings are fixed, not configurable (Principle XI); golden size tests catch
/// drift. Work runs off the request thread and stops when the token is cancelled.
/// </remarks>
internal sealed class VariantEncoder(ImagingRuntime runtime) : IVariantEncoder
{
    /// <summary>A height that never limits: the width alone decides the size.</summary>
    private const int UnboundedHeight = 10_000_000;

    public Task<byte[]> EncodeAsync(Stream original, MediaFileType type, int width, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        // Constructing the runtime applied the loader restrictions; keep the dependency explicit.
        _ = runtime;

        return Task.Run(() => Encode(original, type, width, cancellationToken), cancellationToken);
    }

    private static byte[] Encode(Stream original, MediaFileType type, int width, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var resized = Image.ThumbnailStream(
                original,
                width,
                height: UnboundedHeight,
                size: Enums.Size.Down,
                noRotate: false,
                outputProfile: "srgb");

            // Cancelling the token kills the pipeline between tiles.
            resized.SetProgress(new Progress<int>(_ => { }), cancellationToken);

            var bytes = type.Mime switch
            {
                "image/jpeg" => resized.JpegsaveBuffer(q: 80, optimizeCoding: true, interlace: true, keep: Enums.ForeignKeep.None),
                "image/png" => resized.PngsaveBuffer(compression: 6, keep: Enums.ForeignKeep.None),
                "image/webp" => resized.WebpsaveBuffer(q: 80, effort: 4, keep: Enums.ForeignKeep.None),
                "image/avif" => resized.HeifsaveBuffer(q: 55, compression: Enums.ForeignHeifCompression.Av1, effort: 2, keep: Enums.ForeignKeep.None),
                _ => throw new VariantEncodingException($"'{type.Mime}' has no variants."),
            };

            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        }
        catch (VipsException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Variant production was cancelled.", ex, cancellationToken);
        }
        catch (VipsException ex)
        {
            throw new VariantEncodingException("The image could not be resized.", ex);
        }
    }
}
