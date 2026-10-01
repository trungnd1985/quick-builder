using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Media.Variants;

/// <summary>Bytes to send for a variant address, and whether they are the variant itself.</summary>
internal sealed record VariantBytes(StoredFile Content, bool IsFallback);

/// <summary>
/// Turns a variant address into bytes, for the public route and the admin preview alike
/// (013 research MV-12, MV-16, contracts/public-media.md §1.2).
/// </summary>
internal sealed class VariantResponder(
    VariantService variants,
    IFileStorage storage,
    ImagingRuntime imaging,
    IOptions<MediaOptions> options)
{
    /// <summary>
    /// The variant of <paramref name="file"/> at <paramref name="width"/>, or its original when
    /// the variant cannot be produced; null when the file has no such variant (not found) or
    /// its bytes are missing.
    /// </summary>
    public async Task<VariantBytes?> GetAsync(MediaFile file, int width, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!VariantPlan.Allows(file, width, options.Value.Variants.Widths, imaging.AvifVariants))
        {
            return null;
        }

        var result = await variants.GetAsync(file, width, cancellationToken).ConfigureAwait(false);

        switch (result)
        {
            case VariantResult.Stored stored:
                return new VariantBytes(stored.File, IsFallback: false);

            case VariantResult.Produced produced:
                var stream = new MemoryStream(produced.Bytes, writable: false);
                return new VariantBytes(new StoredFile(stream, produced.Bytes.Length, DateTimeOffset.UtcNow), IsFallback: false);

            default:
                var original = await storage.OpenReadAsync(new StorageKey(file.StorageKey), cancellationToken).ConfigureAwait(false);
                return original is null ? null : new VariantBytes(original, IsFallback: true);
        }
    }
}
