namespace QuickBuilder.Pages.Contracts.Redirects;

/// <summary>
/// Counts redirect use without writing on the request path (017 FR-021a, research SE-12).
/// </summary>
/// <remarks>
/// The Pages module implements it as a singleton that only adds to counters in memory; a
/// background service saves them. A call never blocks, never does I/O and never throws, so
/// the public path stays read-only (constitution Principle VII).
/// </remarks>
public interface IRedirectHits
{
    /// <summary>Counts one use of a redirect, now.</summary>
    void Record(Guid siteId, Guid redirectId);
}
