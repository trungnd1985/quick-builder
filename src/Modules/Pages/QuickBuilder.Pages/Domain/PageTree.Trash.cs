using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain;

/// <summary>What restoring a page will do, computed before anything changes (021 TR-10).</summary>
/// <param name="ParentId">The live parent it goes under; null for the top level.</param>
/// <param name="Position">Its place among its new siblings.</param>
/// <param name="Slug">Its slug; differs from the page's own when the old address is taken (FR-017).</param>
/// <param name="Path">Its address after the restore.</param>
/// <param name="BecomesHome">The site has no home page and the page goes to the top level.</param>
/// <param name="TemplateCollectionId">The collection it is the template of again (TR-12); null otherwise.</param>
internal sealed record PageRestoreOutcome(
    Guid? ParentId,
    int Position,
    string Slug,
    string Path,
    bool BecomesHome,
    Guid? TemplateCollectionId);

/// <summary>Moving pages to the trash and taking them out again (021 TR-05, TR-10, TR-12).</summary>
/// <remarks>
/// The tree loads live pages only (the trash filter applies to <c>Include</c>), so every rule
/// about children, the home page, the page limit and addresses ignores trashed pages without
/// change. A trashed page leaves <see cref="Pages"/> but keeps its row: the caller marks the
/// row modified, so EF does not delete it as an orphan of the tree.
/// </remarks>
internal sealed partial class PageTree
{
    /// <summary>
    /// Moves a page with no children to the trash (021 FR-001), under the guards a delete has
    /// (010 FR-012, FR-013). Returns it, so the caller can mark its row modified.
    /// </summary>
    public Result<Page> Trash(Guid pageId, int version, bool hadSchedule, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Find(pageId) is not { } page)
        {
            return Result.Failure<Page>(PagesErrors.NotFound);
        }

        if (page.Version != version)
        {
            return Result.Failure<Page>(PagesErrors.OutOfDate);
        }

        // Clarification Q1: still refused; trashed children are not loaded, so do not count.
        var childCount = Live.Count(p => p.ParentId == pageId);
        if (childCount > 0)
        {
            return Result.Failure<Page>(PagesErrors.HasChildren(childCount));
        }

        if (page.IsHome && Live.Count > 1)
        {
            return Result.Failure<Page>(PagesErrors.HomePage);
        }

        var now = clock.GetUtcNow();
        var path = page.Path;
        var template = page.TemplateCollectionId;
        var siblings = Children(page.ParentId);
        siblings.Remove(page);
        _pages.Remove(page);
        Renumber(siblings);
        page.MoveToTrash(actor, hadSchedule, now);
        PageCount = Live.Count;
        UpdatedAtUtc = now;

        // TR-12: while it is in the trash, the collection has no template.
        if (template is not null)
        {
            Raise(new PageTemplateChangedEvent(SiteId, page.Id, null, template, now));
        }

        Raise(new PageDeletedEvent(SiteId, page.Id, path, now));
        return Result.Success(page);
    }

    /// <summary>
    /// What restoring <paramref name="page"/> under <paramref name="parentId"/> would do, without
    /// changing anything (021 TR-10). <paramref name="position"/> null puts it last.
    /// </summary>
    /// <param name="page">A trashed page of this site, loaded by the caller.</param>
    /// <param name="parentId">A live page of the tree, or null for the top level.</param>
    /// <param name="position">Where among its siblings; clamped to their count.</param>
    /// <param name="collectionExists">Whether the collection it was the template of still exists (another module's fact).</param>
    public Result<PageRestoreOutcome> PlanRestore(Page page, Guid? parentId, int? position, bool collectionExists)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!page.IsTrashed || page.SiteId != SiteId)
        {
            return Result.Failure<PageRestoreOutcome>(PagesErrors.NotInTrash);
        }

        // Trashed pages do not count toward the limit, so a restore can reach it (TR-10).
        if (Live.Count >= PageAddresses.MaxPagesPerSite)
        {
            return Result.Failure<PageRestoreOutcome>(PagesErrors.TooManyPages);
        }

        Page? parent = null;
        if (parentId is { } parentKey && (parent = Find(parentKey)) is null)
        {
            return Result.Failure<PageRestoreOutcome>(PagesErrors.InvalidPlacement);
        }

        // 016 TB-02: nothing sits under a template.
        if (parent is { IsTemplate: true })
        {
            return Result.Failure<PageRestoreOutcome>(PagesErrors.InvalidPlacement);
        }

        var siblings = Children(parentId);
        var index = Math.Clamp(position ?? siblings.Count, 0, siblings.Count);
        var becomesHome = parentId is null && Home is null;

        // TR-12: back as the template only when the slot is still free.
        Guid? template = null;
        if (page.TrashedTemplateCollectionId is { } collection
            && collectionExists
            && !becomesHome
            && parent is { IsHome: false }
            && !Live.Any(p => p.TemplateCollectionId == collection)
            && !Live.Any(p => p.IsTemplate && p.ParentId == parentId))
        {
            template = collection;
        }

        // FR-017: a taken address gets the next free one, -2, -3 and so on.
        Error? refused = null;
        IReadOnlyList<Planned>? planned = null;
        var slug = CopyNaming.NumberedSlug(page.Slug, candidate =>
        {
            var plan = Plan(Shape.Of, added: new Shape(page.Id, parentId, candidate, becomesHome, index, template is not null));
            if (plan.IsSuccess)
            {
                planned = plan.Value;
                return true;
            }

            if (plan.Error.Code == "pages.duplicate_address" && !becomesHome)
            {
                return false;
            }

            refused = plan.Error;
            return true;
        });

        if (refused is not null)
        {
            return Result.Failure<PageRestoreOutcome>(refused);
        }

        var path = planned!.First(p => p.Id == page.Id).Path;
        return Result.Success(new PageRestoreOutcome(parentId, index, slug, path, becomesHome, template));
    }

    /// <summary>
    /// Takes <paramref name="page"/> out of the trash as <paramref name="outcome"/> says (021
    /// FR-014, FR-016). The caller computed the outcome with <see cref="PlanRestore"/> in the
    /// same transaction and compared it with what the member confirmed.
    /// </summary>
    public Result Restore(Page page, PageRestoreOutcome outcome, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(clock);

        var plan = Plan(Shape.Of, added: new Shape(page.Id, outcome.ParentId, outcome.Slug, outcome.BecomesHome, outcome.Position, outcome.TemplateCollectionId is not null));
        if (plan.IsFailure)
        {
            return Result.Failure(plan.Error);
        }

        // The siblings as they are, before the page is live and could count as one of them.
        var siblings = Children(outcome.ParentId);

        var now = clock.GetUtcNow();
        page.TakeOutOfTrash(actor, now);
        if (outcome.Slug != page.Slug)
        {
            page.Rename(page.Title, outcome.Slug);
        }

        page.Reparent(outcome.ParentId);
        page.SetHome(outcome.BecomesHome);
        page.SetTemplate(outcome.TemplateCollectionId);

        // EF may already have fixed the loaded row up into the navigation.
        if (!_pages.Contains(page))
        {
            _pages.Add(page);
        }
        siblings.Insert(Math.Min(outcome.Position, siblings.Count), page);
        Renumber(siblings);
        Apply(plan.Value);
        PageCount = Live.Count;
        UpdatedAtUtc = now;

        Raise(new PageRestoredEvent(SiteId, page.Id, now));
        if (outcome.BecomesHome)
        {
            Raise(new HomePageChangedEvent(SiteId, page.Id, PreviousPageId: null, now));
        }

        if (outcome.TemplateCollectionId is { } collection)
        {
            Raise(new PageTemplateChangedEvent(SiteId, page.Id, collection, null, now));
        }

        return Result.Success();
    }
}
