namespace QuickBuilder.Sites.Domain;

/// <summary>The scheme of a site's base URL (009 research S-08).</summary>
/// <remarks>
/// Only shapes the base URL. The scheme a visitor actually uses is the proxy's business
/// and arrives through forwarded headers.
/// </remarks>
internal static class SiteScheme
{
    public const string Https = "https";
    public const string Http = "http";

    public static bool TryParse(string? value, out string scheme)
    {
        scheme = value?.Trim().ToLowerInvariant() switch
        {
            Https => Https,
            Http => Http,
            _ => string.Empty,
        };

        return scheme.Length > 0;
    }
}
