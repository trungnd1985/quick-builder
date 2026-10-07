using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// What every Cms handler needs to know about the request beyond its own data: whether the
/// named site exists, who is acting, what they may do, and what people are called.
/// </summary>
/// <remarks>
/// Sites and names come through the other modules' contracts, never their tables (001
/// FR-005), as in <c>PagesAccess</c>. The capability on each endpoint has already decided that
/// the caller may act on the site at all.
/// </remarks>
internal sealed class CmsAccess(ISiteDirectory sites, IAccessEvaluator access, IUserDirectory users)
{
    public async Task<bool> SiteExistsAsync(Guid siteId, CancellationToken cancellationToken) =>
        await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>The signed-in user. Every Cms endpoint requires one.</summary>
    public Guid Actor =>
        access.UserId ?? throw new InvalidOperationException("A Cms operation reached a handler without a signed-in user.");

    /// <summary>Whether the caller may change collections on the site, so the builder can offer it.</summary>
    public Task<bool> CanManageAsync(Guid siteId, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(CmsCapabilities.CollectionsManage, siteId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var found = await users.FindManyAsync(distinct, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(u => u.Id, u => u.DisplayName);
    }
}
