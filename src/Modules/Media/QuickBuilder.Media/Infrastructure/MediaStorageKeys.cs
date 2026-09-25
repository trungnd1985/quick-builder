using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// The Media module's storage keys: <c>{siteId:N}/media/{fileId:N}</c> (003 research M-03).
/// </summary>
/// <remarks>
/// Site first, so all of a site's bytes are one prefix to back up or remove (Principle V,
/// FR-020). The <c>media</c> segment leaves room for other per-site consumers. No part of
/// the user's file name ever appears (FR-021).
/// </remarks>
internal static class MediaStorageKeys
{
    public const string Segment = "media";

    public static StorageKey For(Guid siteId, Guid fileId) =>
        new($"{siteId:N}/{Segment}/{fileId:N}");

    /// <summary>The prefix under which one site's media bytes live.</summary>
    public static string SitePrefix(Guid siteId) => $"{siteId:N}/{Segment}/";

    /// <summary>
    /// The site of a media key, or null when the key is not shaped <c>{32 hex}/media/{32 hex}</c>.
    /// </summary>
    public static Guid? SiteOf(StorageKey key)
    {
        var parts = key.Value.Split('/');
        return parts.Length == 3
            && string.Equals(parts[1], Segment, StringComparison.Ordinal)
            && parts[0].Length == 32 && parts[2].Length == 32
            && Guid.TryParseExact(parts[0], "N", out var siteId)
            && Guid.TryParseExact(parts[2], "N", out _)
                ? siteId
                : null;
    }
}
