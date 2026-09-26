using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The one way a structural change is made: load the site's tree, ask it, publish what it
/// recorded, save — retried as a whole when another change to the site wins the race
/// (research P-02, P-05).
/// </summary>
/// <remarks>
/// Every structural slice goes through here, so none of them can forget to mark the tree
/// changed (which is what makes concurrent changes serialise) or to add the tree's events
/// to the outbox in the same transaction.
/// </remarks>
internal sealed class PageTreeWrites(PagesDbContext database, PagesAccess access, TimeProvider clock)
{
    /// <summary>
    /// Runs <paramref name="change"/> against the site's tree. <paramref name="startIfMissing"/>
    /// starts a tree for a site with no pages yet; otherwise such a site has no pages to change.
    /// </summary>
    public Task<Result<T>> ChangeAsync<T>(
        Guid siteId,
        bool startIfMissing,
        Func<PageTree, Guid, TimeProvider, Result<T>> change,
        Action<PageTree, T>? beforeSave,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var actor = access.Actor;

        return TreeConcurrencyRetry.ExecuteAsync(database, async ct =>
        {
            var tree = await database.ForSite<PageTree>(siteId)
                .Include(t => t.Pages)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (tree is null)
            {
                if (!startIfMissing)
                {
                    return Result.Failure<T>(PagesErrors.NotFound);
                }

                tree = PageTree.Start(siteId, clock);
                database.PageTrees.Add(tree);
            }

            var result = change(tree, actor, clock);
            if (result.IsFailure)
            {
                database.ChangeTracker.Clear();
                return result;
            }

            beforeSave?.Invoke(tree, result.Value);

            if (database.Entry(tree).State != EntityState.Added)
            {
                database.MarkTreeChanged(tree);
            }

            PagesOutbox.AddFrom(database, tree, actor);
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
            return result;
        }, cancellationToken);
    }
}
