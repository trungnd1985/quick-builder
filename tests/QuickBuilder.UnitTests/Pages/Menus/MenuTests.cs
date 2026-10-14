using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.UnitTests.Sites;

namespace QuickBuilder.UnitTests.Pages.Menus;

/// <summary>The <see cref="Menu"/> entity's own rules (024 FR-001, FR-002; data-model §1.1).</summary>
public sealed class MenuTests
{
    private readonly SteppedClock _clock = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_name_is_trimmed()
    {
        var menu = Menu.Create(Guid.NewGuid(), "  Main  ", 0, Guid.NewGuid(), _clock).Value;

        Assert.Equal("Main", menu.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_refused(string? name)
    {
        var result = Menu.Create(Guid.NewGuid(), name, 0, Guid.NewGuid(), _clock);

        Assert.Equal("pages.invalid_menu_name", result.Error?.Code);
    }

    [Fact]
    public void A_name_of_100_characters_is_kept_and_101_is_refused()
    {
        Assert.True(Menu.Create(Guid.NewGuid(), new string('m', 100), 0, Guid.NewGuid(), _clock).IsSuccess);
        Assert.Equal("pages.invalid_menu_name", Menu.Create(Guid.NewGuid(), new string('m', 101), 0, Guid.NewGuid(), _clock).Error?.Code);
    }

    [Fact]
    public void The_fiftieth_menu_is_allowed_and_the_fifty_first_refused()
    {
        Assert.True(Menu.Create(Guid.NewGuid(), "Main", Menu.MaxPerSite - 1, Guid.NewGuid(), _clock).IsSuccess);
        Assert.Equal("pages.menu_limit_reached", Menu.Create(Guid.NewGuid(), "Main", Menu.MaxPerSite, Guid.NewGuid(), _clock).Error?.Code);
    }

    [Fact]
    public void A_rename_records_who_and_when()
    {
        var creator = Guid.NewGuid();
        var renamer = Guid.NewGuid();
        var menu = Menu.Create(Guid.NewGuid(), "Main", 0, creator, _clock).Value;

        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(menu.Rename(" Footer ", renamer, _clock).IsSuccess);

        Assert.Equal(("Footer", renamer, _clock.GetUtcNow()), (menu.Name, menu.UpdatedBy, menu.UpdatedAtUtc));
        Assert.Equal(creator, menu.CreatedBy);
    }

    [Fact]
    public void A_refused_rename_changes_nothing()
    {
        var menu = Menu.Create(Guid.NewGuid(), "Main", 0, Guid.NewGuid(), _clock).Value;

        Assert.True(menu.Rename(" ", Guid.NewGuid(), _clock).IsFailure);
        Assert.Equal("Main", menu.Name);
    }

    [Fact]
    public void A_new_draft_holds_no_entries_in_the_current_version()
    {
        var draft = MenuDraft.CreateEmpty(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _clock);

        Assert.Equal("""{"schemaVersion":1,"entries":[]}""", draft.Document);
        Assert.Equal(1, draft.SchemaVersion);
    }
}
