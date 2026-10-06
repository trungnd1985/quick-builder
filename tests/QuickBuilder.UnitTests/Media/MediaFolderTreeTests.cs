using QuickBuilder.Media.Domain;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Folder depth, cycles and sibling names (013 FR-001, FR-002, FR-004, data-model.md §3.2).</summary>
public sealed class MediaFolderTreeTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static MediaFolder Create(FolderStructure structure, string name, Guid? parent = null)
    {
        var created = structure.Create(Guid.NewGuid(), Site, parent, name, Now, Member);
        Assert.True(created.IsSuccess, created.Error?.Code);
        return created.Value;
    }

    /// <summary>A chain of <paramref name="depth"/> nested folders; returns the deepest.</summary>
    private static MediaFolder Chain(FolderStructure structure, int depth, string prefix = "level")
    {
        MediaFolder? current = null;
        for (var level = 1; level <= depth; level++)
        {
            current = Create(structure, $"{prefix} {level}", current?.Id);
        }

        return current!;
    }

    [Fact]
    public void Folders_nest_eight_levels_and_no_deeper()
    {
        var structure = new FolderStructure([]);
        var eighth = Chain(structure, 8);

        var ninth = structure.Create(Guid.NewGuid(), Site, eighth.Id, "too deep", Now, Member);

        Assert.Equal("media.folder_too_deep", ninth.Error!.Code);
    }

    [Fact]
    public void A_folder_cannot_move_into_itself_or_below_itself()
    {
        var structure = new FolderStructure([]);
        var top = Create(structure, "Top");
        var child = Create(structure, "Child", top.Id);
        var grandchild = Create(structure, "Grandchild", child.Id);

        Assert.Equal("media.folder_cycle", structure.Move(top, top.Id).Error!.Code);
        Assert.Equal("media.folder_cycle", structure.Move(top, grandchild.Id).Error!.Code);
        Assert.Null(top.ParentId);
    }

    [Fact]
    public void A_move_is_refused_when_the_deepest_descendant_would_pass_eight()
    {
        var structure = new FolderStructure([]);
        var subtreeRoot = Chain(structure, 3, "sub");                 // a subtree three deep …
        var root = structure.Find(subtreeRoot.ParentId!.Value)!;
        var host = Chain(structure, 6, "host");                        // … under a folder at depth 6

        Assert.Equal("media.folder_too_deep", structure.Move(structure.Find(root.ParentId!.Value)!, host.Id).Error!.Code);

        var shallowHost = Chain(structure, 5, "shallow");              // 5 + 3 = 8 fits
        Assert.True(structure.Move(structure.Find(root.ParentId!.Value)!, shallowHost.Id).IsSuccess);
    }

    [Theory]
    [InlineData("Photos", "photos")]
    [InlineData("Été", "ETE")]
    [InlineData("Team photos", "  team PHOTOS  ")]
    public void Sibling_names_clash_ignoring_case_and_accents(string first, string second)
    {
        var structure = new FolderStructure([]);
        Create(structure, first);

        Assert.Equal("media.folder_name_taken", structure.Create(Guid.NewGuid(), Site, null, second, Now, Member).Error!.Code);
    }

    [Fact]
    public void The_same_name_is_fine_under_different_parents_and_moving_checks_the_new_siblings()
    {
        var structure = new FolderStructure([]);
        var a = Create(structure, "A");
        var b = Create(structure, "B");
        var inA = Create(structure, "2026", a.Id);
        Create(structure, "2026", b.Id);

        Assert.Equal("media.folder_name_taken", structure.Move(inA, b.Id).Error!.Code);
        Assert.Equal("media.folder_name_taken", structure.Rename(a, "b").Error!.Code);
        Assert.True(structure.Rename(a, "a").IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0007​")]
    [InlineData("a/")]
    public void Unusable_names_are_refused(string name) =>
        Assert.Equal("media.invalid_folder_name", new FolderStructure([]).Create(Guid.NewGuid(), Site, null, name, Now, Member).Error!.Code);

    [Fact]
    public void Names_are_cleaned_and_limited_to_100_characters()
    {
        Assert.Equal("2026 Q4", FolderStructure.CleanName("  photos/2026 Q4\u0007 "));
        Assert.Equal(new string('x', 100), FolderStructure.CleanName(new string('x', 100)));
        Assert.Null(FolderStructure.CleanName(new string('x', 101)));
    }

    [Fact]
    public void A_site_has_at_most_1000_folders()
    {
        var structure = new FolderStructure([]);
        for (var i = 0; i < FolderStructure.MaxFolders; i++)
        {
            Create(structure, $"f{i}");
        }

        Assert.Equal("media.folder_limit", structure.Create(Guid.NewGuid(), Site, null, "one more", Now, Member).Error!.Code);
    }

    [Fact]
    public void Descendants_include_the_folder_itself()
    {
        var structure = new FolderStructure([]);
        var top = Create(structure, "Top");
        var child = Create(structure, "Child", top.Id);
        var grandchild = Create(structure, "Grandchild", child.Id);
        Create(structure, "Unrelated");

        Assert.Equal(new HashSet<Guid> { top.Id, child.Id, grandchild.Id }, structure.Descendants(top.Id));
    }

    [Fact]
    public void An_unknown_parent_is_not_found() =>
        Assert.Equal("media.folder_not_found", new FolderStructure([]).Create(Guid.NewGuid(), Site, Guid.NewGuid(), "x", Now, Member).Error!.Code);
}
