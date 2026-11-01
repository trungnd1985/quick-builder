using QuickBuilder.Cms.Contracts;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Who may view and who may edit each kind of document in an editing session (023 research
/// CE-16). No new capabilities: the ones the owning modules already check.
/// </summary>
/// <remarks>
/// Every editing endpoint declares <see cref="PageContentCapabilities.View"/>, which every
/// site role holds, so a member with no role on the site gets the usual 404 before a handler
/// runs. The handler then asks this for the document's own kind.
/// </remarks>
internal sealed class EditingAccess(IAccessEvaluator access)
{
    public static string ViewCapabilityOf(string kind) => kind switch
    {
        EditingDocumentKinds.Page or EditingDocumentKinds.Component => PageContentCapabilities.View,
        EditingDocumentKinds.Item => ItemCapabilities.CollectionsView,
        _ => throw new ArgumentException($"Unknown document kind '{kind}'.", nameof(kind)),
    };

    public static string EditCapabilityOf(string kind) => kind switch
    {
        EditingDocumentKinds.Page => PageContentCapabilities.DraftsEdit,
        EditingDocumentKinds.Component => PageContentCapabilities.ComponentsManage,
        EditingDocumentKinds.Item => ItemCapabilities.ItemsEdit,
        _ => throw new ArgumentException($"Unknown document kind '{kind}'.", nameof(kind)),
    };

    /// <summary>The signed-in user. Every editing endpoint requires one.</summary>
    public Guid Actor =>
        access.UserId ?? throw new InvalidOperationException("An editing operation reached a handler without a signed-in user.");

    public Task<bool> MayViewAsync(Guid siteId, string kind, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(ViewCapabilityOf(kind), siteId, cancellationToken);

    public Task<bool> MayEditAsync(Guid siteId, string kind, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(EditCapabilityOf(kind), siteId, cancellationToken);
}
