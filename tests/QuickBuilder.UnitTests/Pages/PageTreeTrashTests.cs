using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>Moving pages to the trash and restoring them (021 FR-001, FR-016, FR-017; TR-05, TR-10, TR-12).</summary>
public sealed class PageTreeTrashTests
{
    private readonly PageTreeTestKit _kit = new();

    private Page Trash(Page page)
    {
        var trashed = _kit.Tree.Trash(page.Id, page.Version, hadSchedule: false, PageTreeTestKit.Actor, _kit.Clock);
        Assert.True(trashed.IsSuccess, trashed.IsFailure ? trashed.Error.Code : null);
        return trashed.Value;
    }

    private PageRestoreOutcome Plan(Page page, Guid? parent, int? position = null, bool collectionExists = true)
    {
        var plan = _kit.Tree.PlanRestore(page, parent, position, collectionExists);
        Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error.Code : null);
        return plan.Value;
    }

    private void Restore(Page page, PageRestoreOutcome outcome)
    {
        var restored = _kit.Tree.Restore(page, outcome, PageTreeTestKit.Actor, _kit.Clock);
        Assert.True(restored.IsSuccess, restored.IsFailure ? restored.Error.Code : null);
    }

    [Fact]
    public void A_trashed_page_leaves_the_tree_keeps_where_it_was_and_frees_its_address()
    {
        var hosting = _kit.Add("hosting");
        _kit.Add("plans", hosting);
        var faq = _kit.Add("faq", hosting);
        _kit.Tree.ClearDomainEvents();

        var page = Trash(faq);

        Assert.Equal(["/hosting", "/hosting/plans"], _kit.Paths());
        Assert.Equal(2, _kit.Tree.PageCount);
        Assert.True(page.IsTrashed);
        Assert.Null(page.ParentId);
        Assert.Equal(hosting.Id, page.TrashedParentId);
        Assert.Equal(1, page.TrashedPosition);
        Assert.Equal("/hosting/faq", page.TrashedPath);
        Assert.Equal("/hosting/faq", Assert.IsType<PageDeletedEvent>(Assert.Single(_kit.Tree.DomainEvents)).Path);
        _kit.AssertInvariants();

        _kit.Add("faq", hosting);
        Assert.Contains("/hosting/faq", _kit.Paths());
    }

    [Fact]
    public void A_page_with_live_children_is_refused_and_trashed_children_do_not_count()
    {
        var hosting = _kit.Add("hosting");
        var plans = _kit.Add("plans", hosting);

        var refused = _kit.Tree.Trash(hosting.Id, hosting.Version, false, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal("pages.has_children", refused.Error?.Code);

        Trash(plans);
        Trash(_kit.At("/hosting"));
        Assert.Empty(_kit.Paths());
    }

    [Fact]
    public void Restoring_returns_a_page_to_its_place_among_its_siblings()
    {
        var hosting = _kit.Add("hosting");
        _kit.Add("a", hosting);
        var b = _kit.Add("b", hosting);
        _kit.Add("c", hosting);
        var page = Trash(b);

        Restore(page, Plan(page, page.TrashedParentId, page.TrashedPosition));

        Assert.False(page.IsTrashed);
        Assert.Equal(["/hosting", "/hosting/a", "/hosting/b", "/hosting/c"], _kit.Paths());
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_position_past_the_end_is_clamped()
    {
        var a = _kit.Add("a");
        var b = _kit.Add("b");
        var page = Trash(b);
        Trash(a);

        Assert.Equal(0, Plan(page, null, position: 5).Position);
    }

    [Fact]
    public void A_taken_address_gets_the_next_free_one()
    {
        _kit.Add("home", isHome: true);
        var pricing = _kit.Add("pricing");
        var page = Trash(pricing);
        _kit.Add("pricing");

        var plan = Plan(page, null);

        Assert.Equal("pricing-2", plan.Slug);
        Assert.Equal("/pricing-2", plan.Path);
        Restore(page, plan);
        Assert.Contains("/pricing-2", _kit.Paths());
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_top_level_page_becomes_home_when_the_site_has_none()
    {
        var only = _kit.Add("home", isHome: true);
        var page = Trash(only);

        var plan = Plan(page, null);

        Assert.True(plan.BecomesHome);
        Assert.Equal("/", plan.Path);
        Restore(page, plan);
        Assert.True(page.IsHome);
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_parent_that_is_not_live_is_refused()
    {
        var a = _kit.Add("a");
        var page = Trash(_kit.Add("b"));

        Assert.Equal("trash.invalid_placement", _kit.Tree.PlanRestore(page, Guid.NewGuid(), null, true).Error?.Code);
        Assert.Equal(1, _kit.Tree.Pages.Count(p => p.Id == a.Id));
    }

    [Fact]
    public void Planning_changes_nothing()
    {
        _kit.Add("home", isHome: true);
        var page = Trash(_kit.Add("about"));
        _kit.Tree.ClearDomainEvents();

        Plan(page, null);

        Assert.True(page.IsTrashed);
        Assert.Equal(["/"], _kit.Paths());
        Assert.Empty(_kit.Tree.DomainEvents);
    }

    [Fact]
    public void A_template_comes_back_as_a_normal_page_when_its_collection_is_gone()
    {
        var blog = _kit.Add("blog");
        var template = _kit.Add("post", blog);
        var collection = Guid.CreateVersion7();
        Assert.True(_kit.Tree.SetTemplate(template.Id, collection, template.Version, PageTreeTestKit.Actor, _kit.Clock).IsSuccess);

        var page = Trash(template);
        Assert.Equal(collection, page.TrashedTemplateCollectionId);
        Assert.Null(page.TemplateCollectionId);

        Assert.Null(Plan(page, blog.Id, collectionExists: false).TemplateCollectionId);
        Assert.Equal(collection, Plan(page, blog.Id, collectionExists: true).TemplateCollectionId);
    }
}
