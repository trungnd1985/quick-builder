using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Template pages in the page tree (016 FR-002, FR-003, FR-008, FR-009; research TB-02;
/// tasks T025).
/// </summary>
public sealed class TemplateTreeTests
{
    private static readonly Guid Posts = Guid.Parse("0192f5a0-0000-7000-8000-00000000c001");
    private static readonly Guid People = Guid.Parse("0192f5a0-0000-7000-8000-00000000c002");

    private readonly PageTreeTestKit _kit = new();

    private PageTree Tree => _kit.Tree;

    private Shared.Kernel.Results.Result Make(Page page, Guid? collection) =>
        Tree.SetTemplate(page.Id, collection, page.Version, PageTreeTestKit.Actor, _kit.Clock);

    [Fact]
    public void A_template_address_is_its_parents_address_and_the_slug_placeholder()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        Tree.ClearDomainEvents();

        var made = Make(post, Posts);

        Assert.True(made.IsSuccess, made.Error?.ToString());
        Assert.Equal(("/blog/{slug}", Posts, true), (post.Path, post.TemplateCollectionId, post.IsTemplate));
        Assert.Equal("post", post.Slug);
        Assert.Contains(Tree.DomainEvents, e => e is PageTemplateChangedEvent { CollectionId: { } c, PreviousCollectionId: null } && c == Posts);
        var moved = Assert.Single(Tree.DomainEvents.OfType<PageAddressesChangedEvent>());
        Assert.Equal(("/blog/post", "/blog/{slug}"), (moved.Changes[0].OldPath, moved.Changes[0].NewPath));
        _kit.AssertInvariants();
    }

    [Fact]
    public void Turning_a_template_back_gives_it_its_own_slugs_address()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        Make(post, Posts);

        var back = Make(post, null);

        Assert.True(back.IsSuccess);
        Assert.Equal(("/blog/post", (Guid?)null), (post.Path, post.TemplateCollectionId));
        _kit.AssertInvariants();
    }

    [Fact]
    public void Turning_a_template_back_is_refused_when_its_slugs_address_is_taken()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        Make(post, Posts);

        // The template's own address /blog/post is free while it is a template, so a page may take it.
        _kit.Add("post", blog);

        var back = Make(post, null);

        Assert.Equal("pages.duplicate_address", back.Error?.Code);
        Assert.Equal("/blog/{slug}", post.Path);
    }

    [Fact]
    public void A_template_at_the_top_level_under_the_home_page_or_as_the_home_page_is_refused()
    {
        var home = _kit.Add("home", isHome: true);
        var top = _kit.Add("top");
        var underHome = _kit.Add("under-home", home);

        Assert.Equal("pages.template_needs_parent", Make(top, Posts).Error?.Code);
        Assert.Equal("pages.template_needs_parent", Make(underHome, Posts).Error?.Code);
        Assert.Equal("pages.template_needs_parent", Make(home, Posts).Error?.Code);
        Assert.All(Tree.Pages, p => Assert.False(p.IsTemplate));
    }

    [Fact]
    public void A_page_with_children_cannot_become_a_template_and_nothing_goes_under_a_template()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        _kit.Add("child", post);
        var other = _kit.Add("other", blog);
        var loose = _kit.Add("loose");

        Assert.Equal("pages.template_has_children", Make(post, Posts).Error?.Code);

        Assert.True(Make(other, People).IsSuccess);
        var created = Tree.CreatePage("Under", "under", other.Id, Placement.Last, isHome: false, PageTreeTestKit.Actor, _kit.Clock);
        var moved = Tree.Move(loose.Id, other.Id, null, loose.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.template_has_children", created.Error?.Code);
        Assert.Equal("pages.template_has_children", moved.Error?.Code);
    }

    [Fact]
    public void A_collection_has_one_template_and_a_parent_holds_one()
    {
        var blog = _kit.Add("blog");
        var news = _kit.Add("news");
        var first = _kit.Add("first", blog);
        var second = _kit.Add("second", news);
        var sibling = _kit.Add("sibling", blog);
        Assert.True(Make(first, Posts).IsSuccess);

        var sameCollection = Make(second, Posts);
        var sameParent = Make(sibling, People);

        Assert.Equal("pages.collection_has_template", sameCollection.Error?.Code);
        Assert.Equal("pages.template_parent_taken", sameParent.Error?.Code);
    }

    [Fact]
    public void A_template_moves_with_its_ancestors_and_may_not_leave_its_fixed_segment()
    {
        var resources = _kit.Add("resources");
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        Make(post, Posts);

        Tree.Move(blog.Id, resources.Id, null, blog.Version, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal("/resources/blog/{slug}", post.Path);

        Tree.Rename(resources.Id, "Library", "library", resources.Version, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal("/library/blog/{slug}", post.Path);

        var toTop = Tree.Move(post.Id, null, null, post.Version, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal("pages.template_needs_parent", toTop.Error?.Code);
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_template_cannot_move_beside_another_template()
    {
        var blog = _kit.Add("blog");
        var news = _kit.Add("news");
        var post = _kit.Add("post", blog);
        var story = _kit.Add("story", news);
        Make(post, Posts);
        Make(story, People);

        var moved = Tree.Move(story.Id, blog.Id, null, story.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.template_parent_taken", moved.Error?.Code);
    }

    [Fact]
    public void A_page_holding_a_template_cannot_become_the_home_page()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        Make(post, Posts);

        var made = Tree.MakeHome(blog.Id, blog.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.template_needs_parent", made.Error?.Code);
    }

    [Fact]
    public void Changing_a_templates_collection_announces_both_and_keeps_the_pattern()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);
        Make(post, Posts);
        Tree.ClearDomainEvents();

        Assert.True(Make(post, People).IsSuccess);

        Assert.Equal("/blog/{slug}", post.Path);
        var changed = Assert.IsType<PageTemplateChangedEvent>(Assert.Single(Tree.DomainEvents));
        Assert.Equal((People, Posts), (changed.CollectionId, changed.PreviousCollectionId));
    }

    [Fact]
    public void An_out_of_date_version_is_refused_and_no_change_changes_nothing()
    {
        var blog = _kit.Add("blog");
        var post = _kit.Add("post", blog);

        Assert.Equal("pages.out_of_date", Tree.SetTemplate(post.Id, Posts, post.Version + 1, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);

        var version = post.Version;
        Assert.True(Make(post, null).IsSuccess);
        Assert.Equal(version, post.Version);
    }
}
