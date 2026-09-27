using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>Deleting pages (010 US5, FR-012 to FR-014, clarification Q2).</summary>
public sealed class PageTreeDeleteTests
{
    private readonly PageTreeTestKit _kit = new();

    [Fact]
    public void A_leaf_is_removed_and_its_siblings_close_up()
    {
        _kit.Add("a");
        var b = _kit.Add("b");
        _kit.Add("c");
        _kit.Tree.ClearDomainEvents();

        var deleted = _kit.Tree.Delete(b.Id, b.Version, _kit.Clock);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(["/a", "/c"], _kit.Paths());
        Assert.Equal(2, _kit.Tree.PageCount);
        Assert.Equal("/b", Assert.IsType<PageDeletedEvent>(Assert.Single(_kit.Tree.DomainEvents)).Path);
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_page_with_children_is_refused_with_their_count()
    {
        var hosting = _kit.Add("hosting");
        _kit.Add("plans", hosting);
        _kit.Add("faq", hosting);

        var deleted = _kit.Tree.Delete(hosting.Id, hosting.Version, _kit.Clock);

        Assert.Equal("pages.has_children", deleted.Error?.Code);
        Assert.Equal(2, PagesErrorsProbe.Extension(deleted.Error!, "childCount"));
        Assert.Equal(3, _kit.Tree.PageCount);
    }

    [Fact]
    public void The_home_page_is_deletable_only_when_it_is_the_only_page()
    {
        var home = _kit.Add("home", isHome: true);
        var about = _kit.Add("about");

        Assert.Equal("pages.home_page", _kit.Tree.Delete(home.Id, home.Version, _kit.Clock).Error?.Code);

        _kit.Tree.Delete(about.Id, about.Version, _kit.Clock);
        Assert.True(_kit.Tree.Delete(home.Id, home.Version, _kit.Clock).IsSuccess);
        Assert.Empty(_kit.Tree.Pages);
    }

    [Fact]
    public void An_address_is_free_again_after_its_page_is_deleted()
    {
        var about = _kit.Add("about");
        _kit.Tree.Delete(about.Id, about.Version, _kit.Clock);

        _kit.Add("about");

        Assert.Equal(["/about"], _kit.Paths());
    }
}

/// <summary>Reads the extra problem fields a Pages error carries.</summary>
internal static class PagesErrorsProbe
{
    public static object? Extension(QuickBuilder.Shared.Kernel.Results.Error error, string name) =>
        QuickBuilder.Pages.PagesErrors.ExtensionsOf(error)?.GetValueOrDefault(name);
}
