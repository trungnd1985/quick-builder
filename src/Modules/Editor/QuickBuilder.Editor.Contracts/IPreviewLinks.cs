using System.Net;

namespace QuickBuilder.Editor.Contracts;

/// <summary>
/// Resolves a preview link's secret, on every request to it (022 research PV-08, PV-18;
/// module-surface.md §2.3).
/// </summary>
/// <remarks>
/// Never cached: a revoked or expired link, or one whose creator lost access, stops working at
/// the very next request on every instance (spec FR-009, FR-017). Whether the target can still
/// be read is the caller's to check, through the preview readers: Editor reads no content.
/// </remarks>
public interface IPreviewLinks
{
    /// <summary>
    /// What <paramref name="secret"/> opens, counting the request against the address's
    /// failure windows when it is unknown, and noting when it was last opened.
    /// </summary>
    Task<PreviewLinkResolution> ResolveAsync(string secret, IPAddress? client, CancellationToken cancellationToken = default);
}

/// <summary>How a preview link resolved.</summary>
public abstract record PreviewLinkResolution
{
    /// <summary>An active link, and what it shows.</summary>
    /// <param name="DocumentKind"><c>page</c> or <c>item</c>.</param>
    /// <param name="VersionNumber">The version it shows; null for the draft.</param>
    /// <param name="SampleItemId">For a template page, the item it shows.</param>
    public sealed record Found(
        Guid LinkId,
        Guid SiteId,
        string DocumentKind,
        Guid DocumentId,
        int? VersionNumber,
        Guid? SampleItemId,
        DateTimeOffset ExpiresAtUtc) : PreviewLinkResolution;

    /// <summary>Unknown, revoked, expired, invalidated by the operator, or its creator lost access.</summary>
    public sealed record NotAvailable : PreviewLinkResolution;

    /// <summary>Too many unknown links from this address group (PV-18).</summary>
    public sealed record Throttled(TimeSpan RetryAfter) : PreviewLinkResolution;
}

/// <summary>The preview-link capabilities, by name (022 research PV-09).</summary>
public static class PreviewCapabilities
{
    /// <summary>
    /// List and revoke every preview link of a site (spec FR-015). Granted to no site role:
    /// Administrator only. The name never changes once shipped.
    /// </summary>
    public const string Manage = "editor.preview_links.manage";
}
