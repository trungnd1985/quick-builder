using QuickBuilder.Editor.Infrastructure;

namespace QuickBuilder.UnitTests.Editor;

/// <summary>The kind-to-capability table of 023 research CE-16. No new capability, and no other kind.</summary>
public sealed class EditingAccessTests
{
    [Theory]
    [InlineData("page", "pages.content.view", "pages.page_drafts.edit")]
    [InlineData("component", "pages.content.view", "pages.components.manage")]
    [InlineData("item", "cms.collections.view", "cms.items.edit")]
    public void Each_kind_uses_the_capabilities_its_owning_module_checks(string kind, string view, string edit)
    {
        Assert.Equal(view, EditingAccess.ViewCapabilityOf(kind));
        Assert.Equal(edit, EditingAccess.EditCapabilityOf(kind));
    }

    [Fact]
    public void An_unknown_kind_has_no_capability()
    {
        Assert.Throws<ArgumentException>(() => EditingAccess.ViewCapabilityOf("menu"));
        Assert.Throws<ArgumentException>(() => EditingAccess.EditCapabilityOf("menu"));
    }
}
