namespace QuickBuilder.Media.Domain;

/// <summary>
/// Which resized copies a file has (013 research MV-11, data-model.md §3.1).
/// </summary>
/// <remarks>
/// Pure and shared: the public reader, the admin mapper, serving and the thumbnail worker all
/// ask the same questions. A file has variants when it is a still JPEG, PNG, WebP or AVIF
/// (AVIF only where libvips can write it) whose dimensions are recorded and within the limit,
/// and whose variants have not been withdrawn. Its variants are the configured widths
/// strictly narrower than it: the original is its own largest size, so nothing is enlarged.
/// </remarks>
internal static class VariantPlan
{
    private static readonly HashSet<string> StillFormats = [MediaFileType.Jpeg.Mime, MediaFileType.Png.Mime, MediaFileType.WebP.Mime];

    public static bool IsEligible(
        string contentType,
        bool isAnimated,
        MediaMetadataState metadataState,
        MediaVariantsState variantsState,
        bool avifSupported)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        var format = StillFormats.Contains(contentType) || (avifSupported && contentType == MediaFileType.Avif.Mime);
        return format
            && !isAnimated
            && metadataState == MediaMetadataState.Recorded
            && variantsState == MediaVariantsState.Available;
    }

    public static bool IsEligible(MediaFile file, bool avifSupported)
    {
        ArgumentNullException.ThrowIfNull(file);
        return IsEligible(file.ContentType, file.IsAnimated, file.MetadataState, file.VariantsState, avifSupported);
    }

    /// <summary>The configured widths strictly narrower than <paramref name="width"/>, ascending.</summary>
    public static IReadOnlyList<int> Widths(int? width, IReadOnlyList<int> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        return width is { } w ? [.. configured.Where(c => c < w).Order()] : [];
    }

    /// <summary>The widths <paramref name="file"/> has variants at; empty when it has none.</summary>
    public static IReadOnlyList<int> For(MediaFile file, IReadOnlyList<int> configured, bool avifSupported) =>
        IsEligible(file, avifSupported) ? Widths(file.Width, configured) : [];

    /// <summary>
    /// Whether a page should offer the original in <c>srcset</c> too: only when it is no wider
    /// than the largest configured width, so a huge original is never downloaded
    /// (contracts/public-media.md §2, SC-003).
    /// </summary>
    public static bool OfferOriginal(int? width, IReadOnlyList<int> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        return width is { } w && configured.Count > 0 && w <= configured.Max();
    }

    /// <summary>Whether <paramref name="width"/> may be requested for <paramref name="file"/>.</summary>
    public static bool Allows(MediaFile file, int width, IReadOnlyList<int> configured, bool avifSupported) =>
        For(file, configured, avifSupported).Contains(width);
}
