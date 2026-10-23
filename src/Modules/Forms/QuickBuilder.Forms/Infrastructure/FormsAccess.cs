using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// What every Forms admin handler needs to know beyond its own data: whether the named site
/// exists, who is acting, and what they may do (018 research FS-20).
/// </summary>
/// <remarks>
/// The capability on each endpoint has already decided that the caller may act on the site at
/// all; this answers the finer "which controls to offer", as <c>PagesAccess</c> does.
/// </remarks>
internal sealed class FormsAccess(ISiteDirectory sites, IAccessEvaluator access)
{
    public async Task<SiteDto?> SiteAsync(Guid siteId, CancellationToken cancellationToken) =>
        await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false);

    /// <summary>The signed-in user. Every Forms admin endpoint requires one.</summary>
    public Guid Actor =>
        access.UserId ?? throw new InvalidOperationException("A Forms operation reached a handler without a signed-in user.");

    /// <summary>Whether the caller may delete submissions (clarification 1).</summary>
    public Task<bool> MayDeleteAsync(Guid siteId, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(FormsCapabilities.SubmissionsDelete, siteId, cancellationToken);
}
