using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Features;
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
internal sealed class PageTreeWrites(
    PagesDbContext database,
    PagesAccess access,
    GenerationBump bump,
    CollectionUseRows uses,
    RedirectWrites redirectWrites,
    RedirectOffers redirectOffers,
    TimeProvider clock)
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
        CancellationToken cancellationToken) =>
        ChangeAsync(
            siteId,
            startIfMissing,
            change,
            beforeSave is null ? null : (tree, value, _) =>
            {
                beforeSave(tree, value);
                return Task.CompletedTask;
            },
            cancellationToken);

    /// <summary>
    /// As the other overload, with a step that writes set-based statements of its own (012:
    /// publishing records, render generations). It runs in the same transaction as the save,
    /// so the tree change and those statements commit together or not at all.
    /// </summary>
    public Task<Result<T>> ChangeAsync<T>(
        Guid siteId,
        bool startIfMissing,
        Func<PageTree, Guid, TimeProvider, Result<T>> change,
        Func<PageTree, T, CancellationToken, Task>? beforeSave,
        CancellationToken cancellationToken) =>
        RunAsync(siteId, startIfMissing, Sync(change), beforeSave, outcome: null, cancellationToken);

    /// <summary>
    /// As the other overloads, on an existing tree, with a change that reads before it decides: a
    /// restore loads the trashed page in the same transaction as the tree (021 TR-10).
    /// </summary>
    public Task<Result<T>> ChangeReadingAsync<T>(
        Guid siteId,
        Func<PageTree, Guid, TimeProvider, CancellationToken, Task<Result<T>>> change,
        Func<PageTree, T, CancellationToken, Task>? beforeSave,
        CancellationToken cancellationToken) =>
        RunAsync(siteId, startIfMissing: false, change, beforeSave, outcome: null, cancellationToken);

    private static Func<PageTree, Guid, TimeProvider, CancellationToken, Task<Result<T>>> Sync<T>(Func<PageTree, Guid, TimeProvider, Result<T>> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return (tree, actor, clock, _) => Task.FromResult(change(tree, actor, clock));
    }

    /// <summary>
    /// As <see cref="ChangeAsync{T}(Guid, bool, Func{PageTree, Guid, TimeProvider, Result{T}}, Func{PageTree, T, CancellationToken, Task}?, CancellationToken)"/>
    /// on an existing tree, also adding the redirects the change offers when the member accepted
    /// them (017 FR-028 to FR-031, research SE-11): in the same transaction, so the old addresses
    /// reach the new content from the next request. Without acceptance the outcome is null.
    /// </summary>
    public async Task<Result<(T Value, RedirectOutcomeResponse? Redirects)>> ChangeOfferingRedirectsAsync<T>(
        Guid siteId,
        Func<PageTree, Guid, TimeProvider, Result<T>> change,
        Func<PageTree, T, CancellationToken, Task>? beforeSave,
        bool redirectOldAddresses,
        CancellationToken cancellationToken)
    {
        var outcome = redirectOldAddresses ? new System.Runtime.CompilerServices.StrongBox<RedirectOutcomeResponse?>() : null;
        var result = await RunAsync(siteId, startIfMissing: false, Sync(change), beforeSave, outcome, cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? Result.Failure<(T, RedirectOutcomeResponse?)>(result.Error)
            : Result.Success((result.Value, outcome?.Value));
    }

    private Task<Result<T>> RunAsync<T>(
        Guid siteId,
        bool startIfMissing,
        Func<PageTree, Guid, TimeProvider, CancellationToken, Task<Result<T>>> change,
        Func<PageTree, T, CancellationToken, Task>? beforeSave,
        System.Runtime.CompilerServices.StrongBox<RedirectOutcomeResponse?>? outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var actor = access.Actor;

        return TreeConcurrencyRetry.ExecuteAsync(database, async ct =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

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

            var before = outcome is null ? null : Snapshot(tree);
            var result = await change(tree, actor, clock, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                database.ChangeTracker.Clear();
                return result;
            }

            if (beforeSave is not null)
            {
                await beforeSave(tree, result.Value, ct).ConfigureAwait(false);
            }

            if (outcome is not null)
            {
                outcome.Value = await OfferRedirectsAsync(siteId, tree, before!, actor, ct).ConfigureAwait(false);
            }

            // 012 FR-011, data-model §3.2: a page's new address reaches every live page that
            // links to it at their next request, on every instance. Read before the events
            // go to the outbox, which clears them.
            await BumpLinkersAsync(siteId, tree, ct).ConfigureAwait(false);

            // 017 research SE-15: an address, a template or a page's existence may have changed
            // what the sitemap lists. A spare rebuild costs little; a missed one shows stale data.
            await bump.Sitemap.BumpAsync(siteId, ct).ConfigureAwait(false);

            if (database.Entry(tree).State != EntityState.Added)
            {
                database.MarkTreeChanged(tree);
            }

            PagesOutbox.AddFrom(database, tree, actor);
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }, cancellationToken);
    }

    /// <summary>Every page's address, parent and whether it is a template.</summary>
    private static Dictionary<Guid, TreeAddress> Snapshot(PageTree tree) =>
        tree.Pages.ToDictionary(p => p.Id, p => new TreeAddress(p.Path, p.ParentId, p.TemplateCollectionId is not null));

    /// <summary>The redirects the change offers, applied against the site's redirects as the tree now is.</summary>
    private async Task<RedirectOutcomeResponse> OfferRedirectsAsync(
        Guid siteId, PageTree tree, IReadOnlyDictionary<Guid, TreeAddress> before, Guid actor, CancellationToken cancellationToken)
    {
        var live = await database.ForSite<Publication>(siteId)
            .Where(p => p.SubjectKind == SubjectKinds.Page && p.LiveVersionId != null)
            .Select(p => p.SubjectId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        var offers = OfferedRedirects.Plan(before, Snapshot(tree), live);
        if (offers.Count == 0)
        {
            return RedirectOutcomeResponse.None;
        }

        var pages = tree.Pages.ToDictionary(p => p.Id, p => new PageAddress(p.Path, live.Contains(p.Id), p.TemplateCollectionId is not null));
        var patterns = tree.Pages
            .Where(p => live.Contains(p.Id) && p.TemplateCollectionId is not null)
            .ToDictionary(p => p.TemplateCollectionId!.Value, p => p.Path);
        var state = await redirectWrites.LoadAsync(siteId, pages, patterns, cancellationToken).ConfigureAwait(false);
        return await redirectOffers.ApplyAsync(state, offers, actor, cancellationToken).ConfigureAwait(false);
    }

    private async Task BumpLinkersAsync(Guid siteId, PageTree tree, CancellationToken cancellationToken)
    {
        var moved = tree.DomainEvents
            .SelectMany(e => e switch
            {
                PageAddressesChangedEvent changed => changed.Changes.Select(c => c.PageId),
                HomePageChangedEvent home => home.PreviousPageId is { } previous ? [home.PageId, previous] : new[] { home.PageId },
                _ => [],
            })
            .Distinct()
            .Select(id => (DependencyKinds.Page, id))
            .ToList();

        await bump.BumpForTargetsAsync(siteId, moved, exceptPageId: null, cancellationToken).ConfigureAwait(false);

        // 016 TB-10 row 3: a template assigned, removed or moved changes the item links of every
        // list of its collection, and the item pages themselves.
        var templateChanges = tree.DomainEvents.OfType<PageTemplateChangedEvent>().ToList();
        var templates = tree.Pages.Where(p => p.TemplateCollectionId is not null).ToDictionary(p => p.Id, p => p.TemplateCollectionId!.Value);
        var collections = templateChanges
            .SelectMany(e => new[] { e.CollectionId, e.PreviousCollectionId })
            .OfType<Guid>()
            .Concat(moved.Where(m => templates.ContainsKey(m.id)).Select(m => templates[m.id]))
            .ToHashSet();

        if (collections.Count > 0)
        {
            await bump.BumpForCollectionsAsync(siteId, collections, includeTemplates: true, exceptPageId: null, cancellationToken).ConfigureAwait(false);
        }

        foreach (var change in templateChanges)
        {
            await RetargetTemplateDependencyAsync(siteId, change, cancellationToken).ConfigureAwait(false);

            // 016 TB-13: a template uses its collection in every state of the page.
            await uses.RestagePageAsync(siteId, change.PageId, change.CollectionId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A live template depends on its collection; the row follows the page's template (TB-10).</summary>
    private async Task RetargetTemplateDependencyAsync(Guid siteId, PageTemplateChangedEvent change, CancellationToken cancellationToken)
    {
        await database.ForSite<LiveDependency>(siteId)
            .Where(d => d.SubjectKind == SubjectKinds.Page && d.SubjectId == change.PageId && d.TargetKind == DependencyKinds.Template)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (change.CollectionId is not { } collectionId)
        {
            return;
        }

        var live = await database.ForSite<Publication>(siteId)
            .AnyAsync(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == change.PageId && p.LiveVersionId != null, cancellationToken)
            .ConfigureAwait(false);
        if (live)
        {
            database.LiveDependencies.Add(new LiveDependency(siteId, SubjectKinds.Page, change.PageId, DependencyKinds.Template, collectionId));
        }
    }
}
