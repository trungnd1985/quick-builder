using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Domain.Events;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Moves on the render stamp of the items whose page shows another item that changed (016
/// research TB-10, data-model §1.4 case 2).
/// </summary>
/// <remarks>
/// An item page can show a field of an item it refers to ("Author → Name"). When that item is
/// published, rolled back or unpublished, every item whose <em>published</em> values refer to it
/// gets a new stamp in the same transaction, so its page's cache key changes at the next request
/// on every instance — no event, no other module. References held only by drafts change nothing
/// visitors see. One set-based <c>UPDATE</c>, with the site named explicitly.
/// </remarks>
internal static class RenderStampBump
{
    /// <summary>The items among <paramref name="written"/> and <paramref name="deleted"/> whose published data changed.</summary>
    public static IReadOnlyList<Guid> PublishedChanges(IEnumerable<Item> written, IEnumerable<Item> deleted) =>
        [.. written.Concat(deleted)
            .Where(item => item.DomainEvents.Any(e => e is ItemPublishedEvent or ItemUnpublishedEvent or ItemDeletedEvent { WasPublished: true }))
            .Select(item => item.Id)
            .Distinct()];

    /// <summary>Moves on the stamp of every item whose published values refer to one of <paramref name="changed"/>.</summary>
    public static Task<int> BumpReferrersAsync(CmsDbContext database, Guid siteId, IReadOnlyCollection<Guid> changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(changed);

        if (changed.Count == 0)
        {
            return Task.FromResult(0);
        }

        var referrers = database.ForSite<ItemReference>(siteId)
            .Where(r => r.InPublished && changed.Contains(r.TargetItemId))
            .Select(r => r.SourceItemId);

        return database.ForSite<Item>(siteId)
            .Where(i => referrers.Contains(i.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RenderStamp, i => i.RenderStamp + 1), cancellationToken);
    }
}
