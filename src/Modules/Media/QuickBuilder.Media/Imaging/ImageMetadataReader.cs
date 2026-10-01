using NetVips;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Features.UploadFile;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Imaging;

/// <summary>What an image's header says, or why it could not be used.</summary>
internal readonly record struct ProbedImage(ImageMetadata? Metadata, MediaMetadataState State);

/// <summary>
/// Reads an image's displayed size and whether it is animated from its header, without
/// decoding any pixel (013 research MV-10).
/// </summary>
/// <remarks>
/// libvips opens images lazily: until pixels are asked for, it has read only the header. The
/// orientation swap follows EXIF: orientations 5 to 8 turn the image a quarter, so the
/// displayed width is the stored height. An image over the pixel limit is reported as such
/// before anything could decode it, and a header libvips cannot read is reported as
/// unreadable rather than thrown.
/// </remarks>
internal static class ImageMetadataReader
{
    public static ProbedImage Read(Stream content, long maxPixels)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            using var image = Image.NewFromStream(content, access: Enums.Access.Sequential);

            var orientation = image.GetTypeOf("orientation") != IntPtr.Zero ? (int)image.Get("orientation") : 1;
            var turned = orientation is >= 5 and <= 8;
            var width = turned ? image.Height : image.Width;
            var height = turned ? image.Width : image.Height;

            if (width <= 0 || height <= 0)
            {
                return new ProbedImage(null, MediaMetadataState.Unreadable);
            }

            if ((long)width * height > maxPixels)
            {
                return new ProbedImage(null, MediaMetadataState.OverLimit);
            }

            var pages = image.GetTypeOf("n-pages") != IntPtr.Zero ? (int)image.Get("n-pages") : 1;
            return new ProbedImage(new ImageMetadata(width, height, isAnimated: pages > 1), MediaMetadataState.Recorded);
        }
        catch (VipsException)
        {
            return new ProbedImage(null, MediaMetadataState.Unreadable);
        }
    }
}

/// <summary>
/// <see cref="IImageMetadataProbe"/> over stored bytes and libvips (013 research MV-10).
/// </summary>
internal sealed class NetVipsImageMetadataProbe(IFileStorage storage, ImagingRuntime runtime) : IImageMetadataProbe
{
    public async Task<Result<ProbedImage>> ProbeAsync(StorageKey key, long maxPixels, CancellationToken cancellationToken)
    {
        // Constructing the runtime applies the loader restrictions before any image is opened.
        _ = runtime;

        var stored = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return Result.Success(new ProbedImage(null, MediaMetadataState.Unreadable));
        }

        await using (stored.ConfigureAwait(false))
        {
            // libvips reads synchronously from the stream; keep it off the request thread.
            var probed = await Task.Run(() => ImageMetadataReader.Read(stored.Content, maxPixels), cancellationToken).ConfigureAwait(false);

            return probed.State == MediaMetadataState.OverLimit
                ? Result.Failure<ProbedImage>(MediaErrors.DimensionsTooLarge)
                : Result.Success(probed);
        }
    }
}
