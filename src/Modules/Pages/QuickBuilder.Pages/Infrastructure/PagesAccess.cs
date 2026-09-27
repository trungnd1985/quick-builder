using QuickBuilder.Identity.Contracts;
using QuickBuilder.Pages.Features;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// What every Pages handler needs to know about the request beyond its own data: whether
/// the named site exists, who is acting, what they may do, and what people are called.
/// </summary>
/// <remarks>
/// Sites and names come through the other modules' contracts, never their tables (001
/// FR-005). The capability on each endpoint has already decided that the caller may act on
/// the site at all; this answers the finer "which controls to offer" (research P-13).
/// </remarks>
internal sealed class PagesAccess(ISiteDirectory sites, IAccessEvaluator access, IUserDirectory users)
{
    public async Task<bool> SiteExistsAsync(Guid siteId, CancellationToken cancellationToken) =>
        await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>The signed-in user. Every Pages endpoint requires one.</summary>
    public Guid Actor =>
        access.UserId ?? throw new InvalidOperationException("A Pages operation reached a handler without a signed-in user.");

    public async Task<PermissionsResponse> PermissionsAsync(Guid siteId, CancellationToken cancellationToken) =>
        new(
            await access.HasCapabilityAsync(PagesCapabilities.StructureManage, siteId, cancellationToken).ConfigureAwait(false),
            await access.HasCapabilityAsync(PagesCapabilities.PageDraftsEdit, siteId, cancellationToken).ConfigureAwait(false),
            await access.HasCapabilityAsync(PagesCapabilities.ComponentsManage, siteId, cancellationToken).ConfigureAwait(false));

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

    public static UserReferenceResponse Reference(Guid userId, IReadOnlyDictionary<Guid, string> names) =>
        new(userId, names.GetValueOrDefault(userId));
}
