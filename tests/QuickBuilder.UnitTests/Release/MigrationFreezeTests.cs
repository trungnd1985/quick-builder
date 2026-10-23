extern alias releasetool;

using releasetool::QuickBuilder.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// <c>qb-release migrations check</c>: a migration that shipped never changes
/// (019 FR-017, SC-008, research RL-12).
/// </summary>
public sealed class MigrationFreezeTests : IDisposable
{
    private const string Shipped = "src/Modules/Pages/QuickBuilder.Pages/Infrastructure/Migrations/20261001000000_AddThing.cs";
    private const string ShippedDesigner = "src/Modules/Pages/QuickBuilder.Pages/Infrastructure/Migrations/20261001000000_AddThing.Designer.cs";
    private const string Snapshot = "src/Modules/Pages/QuickBuilder.Pages/Infrastructure/Migrations/PagesDbContextModelSnapshot.cs";
    private const string Platform = "src/Shared/QuickBuilder.Shared.Infrastructure/Migrations/Platform/Migrations/20261023000000_InitialPlatform.cs";

    private readonly TempRepository _repo = new();

    public MigrationFreezeTests() =>
        _repo.Write(Shipped, "up();").Write(ShippedDesigner, "model").Write(Snapshot, "snapshot v1").Write(Platform, "platform").Commit("0.1.0").Tag("v0.1.0");

    public void Dispose() => _repo.Dispose();

    private IReadOnlyList<string> Problems(string? since = "v0.1.0") => MigrationFreeze.Check(_repo.Root, since).Problems;

    [Fact]
    public void Nothing_changed_passes() => Assert.Empty(Problems());

    [Fact]
    public void Adding_a_migration_and_changing_the_snapshot_pass()
    {
        _repo.Write("src/Modules/Pages/QuickBuilder.Pages/Infrastructure/Migrations/20261101000000_AddMore.cs", "up();")
            .Write(Snapshot, "snapshot v2").Commit();

        Assert.Empty(Problems());
    }

    [Theory]
    [InlineData(Shipped)]
    [InlineData(ShippedDesigner)]
    [InlineData(Platform)]
    public void Editing_a_shipped_migration_fails_naming_it(string path)
    {
        _repo.Write(path, "edited").Commit();

        Assert.Contains(Problems(), p => p.Contains(path, StringComparison.Ordinal) && p.Contains("modified", StringComparison.Ordinal));
    }

    [Fact]
    public void Deleting_or_renaming_a_shipped_migration_fails()
    {
        _repo.Delete(ShippedDesigner).Commit();
        _repo.Move(Shipped, Shipped.Replace("AddThing", "AddOther", StringComparison.Ordinal)).Commit();

        var problems = Problems();
        Assert.Contains(problems, p => p.Contains(ShippedDesigner, StringComparison.Ordinal) && p.Contains("deleted", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains(Shipped, StringComparison.Ordinal) && p.Contains("renamed", StringComparison.Ordinal));
    }

    [Fact]
    public void An_uncommitted_edit_is_seen_too()
    {
        _repo.Write(Shipped, "edited, not committed");

        Assert.NotEmpty(Problems());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v9.9.9")]
    public void With_no_release_yet_the_check_passes_with_a_notice(string? since)
    {
        var result = MigrationFreeze.Check(_repo.Root, since);

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Notice);
    }
}
