using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.UnitTests.Sites;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>Shared components (010 US4, FR-024, FR-025).</summary>
public sealed class ComponentTests
{
    private readonly SteppedClock _clock = new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _site = Guid.CreateVersion7();

    [Fact]
    public void A_name_is_trimmed_and_creating_raises_component_created()
    {
        var component = Component.Create(_site, "  Header ", PageTreeTestKit.Actor, _clock).Value;

        Assert.Equal("Header", component.Name);
        Assert.Equal(component.Id, Assert.IsType<ComponentCreatedEvent>(Assert.Single(component.DomainEvents)).ComponentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_name_is_refused(string? name)
    {
        Assert.Equal("pages.invalid_component_name", Component.Create(_site, name, PageTreeTestKit.Actor, _clock).Error?.Code);
    }

    [Fact]
    public void A_name_over_100_characters_is_refused()
    {
        Assert.True(Component.Create(_site, new string('a', 100), PageTreeTestKit.Actor, _clock).IsSuccess);
        Assert.Equal(
            "pages.invalid_component_name",
            Component.Create(_site, new string('a', 101), PageTreeTestKit.Actor, _clock).Error?.Code);
    }

    [Fact]
    public void Renaming_raises_nothing_and_deleting_raises_component_deleted()
    {
        var component = Component.Create(_site, "Header", PageTreeTestKit.Actor, _clock).Value;
        component.ClearDomainEvents();

        Assert.True(component.Rename("Site header", PageTreeTestKit.Actor, _clock).IsSuccess);
        Assert.Empty(component.DomainEvents);
        Assert.Equal("Site header", component.Name);

        component.MarkDeleted(_clock);
        Assert.IsType<ComponentDeletedEvent>(Assert.Single(component.DomainEvents));
    }

    [Fact]
    public void A_new_draft_holds_the_empty_version_1_document()
    {
        var draft = ComponentDraft.CreateEmpty(_site, Guid.CreateVersion7(), PageTreeTestKit.Actor, _clock);

        Assert.Equal(1, draft.SchemaVersion);
        Assert.Equal(Draft.EmptyDocument, draft.Document);
    }
}
