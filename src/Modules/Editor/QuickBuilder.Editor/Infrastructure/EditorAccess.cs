using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// What every Editor handler needs beyond its own data: whether the named site exists, who is
/// acting, whether they may change the tokens, and what people are called.
/// </summary>
/// <remarks>
/// Sites and names come through the other modules' contracts, never their tables (001
/// FR-005). The capability on each endpoint has already decided the caller may act on the
/// site at all; this answers the finer "which controls to offer".
/// </remarks>
internal sealed class EditorAccess(ISiteDirectory sites, IAccessEvaluator access, IUserDirectory users)
{
    public async Task<bool> SiteExistsAsync(Guid siteId, CancellationToken cancellationToken) =>
        await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>The signed-in user. Every Editor endpoint requires one.</summary>
    public Guid Actor =>
        access.UserId ?? throw new InvalidOperationException("An Editor operation reached a handler without a signed-in user.");

    public Task<bool> CanManageAsync(Guid siteId, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(EditorCapabilities.DesignTokensManage, siteId, cancellationToken);

    public async Task<string?> NameOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await users.FindManyAsync([userId], cancellationToken).ConfigureAwait(false);
        return found.FirstOrDefault(u => u.Id == userId)?.DisplayName;
    }
}
