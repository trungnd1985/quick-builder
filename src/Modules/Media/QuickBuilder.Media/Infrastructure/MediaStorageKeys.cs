using System.Globalization;
using System.Text.RegularExpressions;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// The Media module's storage keys (003 research M-03, 013 research MV-14).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Version 1's original: <c>{siteId:N}/media/{fileId:N}</c>, unchanged from 003.</item>
/// <item>Version n ≥ 2: <c>{siteId:N}/media/{fileId:N}-v{n}</c>. A sibling rather than a
/// sub-key, because on the local provider <c>{fileId}</c> is a file and cannot also be a
/// directory.</item>
/// <item>A variant: <c>{siteId:N}/media-variants/{fileId:N}-v{n}-w{width}</c>. Under its own
/// prefix, so the original sweep never mistakes a variant for an orphaned original.</item>
/// </list>
/// Site first, so all of a site's bytes are one prefix to back up or remove (Principle V,
/// FR-020). No part of a user's file name ever appears (FR-021). The layout is identical on
/// every provider, which is what makes moving between providers a plain copy (FR-037).
/// </remarks>
internal static partial class MediaStorageKeys
{
    public const string Segment = "media";

    public const string VariantSegment = "media-variants";

    /// <summary>Version 1's key.</summary>
    public static StorageKey For(Guid siteId, Guid fileId) =>
        new($"{siteId:N}/{Segment}/{fileId:N}");

    /// <summary>The key of <paramref name="version"/>'s original.</summary>
    public static StorageKey For(Guid siteId, Guid fileId, int version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        return version == 1
            ? For(siteId, fileId)
            : new StorageKey($"{siteId:N}/{Segment}/{fileId:N}-v{version.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>The key of a variant of <paramref name="version"/> at <paramref name="width"/>.</summary>
    public static StorageKey Variant(Guid siteId, Guid fileId, int version, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        return new StorageKey(
            $"{siteId:N}/{VariantSegment}/{fileId:N}-v{version.ToString(CultureInfo.InvariantCulture)}-w{width.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>The prefix under which one site's originals live.</summary>
    public static string SitePrefix(Guid siteId) => $"{siteId:N}/{Segment}/";

    /// <summary>The prefix under which one site's variants live.</summary>
    public static string VariantSitePrefix(Guid siteId) => $"{siteId:N}/{VariantSegment}/";

    /// <summary>
    /// The site of an original's key, or null when the key is not shaped
    /// <c>{32 hex}/media/{32 hex}[-v{n}]</c>.
    /// </summary>
    public static Guid? SiteOf(StorageKey key)
    {
        var match = OriginalPattern().Match(key.Value);
        return match.Success ? Guid.ParseExact(match.Groups["site"].Value, "N") : null;
    }

    /// <summary>The parts of a variant key, or null when it is not one.</summary>
    public static VariantKey? ParseVariant(StorageKey key)
    {
        var match = VariantPattern().Match(key.Value);
        if (!match.Success)
        {
            return null;
        }

        return new VariantKey(
            Guid.ParseExact(match.Groups["site"].Value, "N"),
            Guid.ParseExact(match.Groups["file"].Value, "N"),
            int.Parse(match.Groups["v"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["w"].Value, NumberStyles.None, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("^(?<site>[0-9a-f]{32})/media/[0-9a-f]{32}(?:-v[1-9][0-9]{0,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex OriginalPattern();

    [GeneratedRegex(
        "^(?<site>[0-9a-f]{32})/media-variants/(?<file>[0-9a-f]{32})-v(?<v>[1-9][0-9]{0,4})-w(?<w>[1-9][0-9]{1,3})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VariantPattern();
}

/// <summary>What a variant key names.</summary>
internal readonly record struct VariantKey(Guid SiteId, Guid FileId, int Version, int Width);
