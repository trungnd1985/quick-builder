using NetArchTest.Rules;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Every item write goes through <c>ItemWrites</c> (015 research IT-06, module-surface.md §5).
/// </summary>
/// <remarks>
/// 014 made the write gate the one way into writing a collection's items, so a write can never
/// land in a shape a running schema change has just left. Two rules keep it so. Item slices
/// never touch the database facade, which is where transactions and raw SQL come from, so they
/// cannot write outside the transaction <c>ItemWrites</c> opens behind the gate; and only
/// <c>ItemWrites</c> and the index backfill, which takes the gate the same way, enter it at all.
/// Reads through <c>DbSet</c>s stay free.
/// </remarks>
public sealed class ItemWritesOnlyTests
{
    private static readonly System.Reflection.Assembly Cms = typeof(Cms.CmsModule).Assembly;

    [Fact]
    public void Item_slices_never_open_their_own_transactions_or_raw_sql()
    {
        var offenders = Types.InAssembly(Cms)
            .That().ResideInNamespace("QuickBuilder.Cms.Features.Items")
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade")
            .GetResult()
            .FailingTypes?
            .Select(t => t.FullName!)
            .ToList() ?? [];

        Assert.True(offenders.Count == 0, "These item slices use the database facade; write through ItemWrites:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Only_item_writes_and_the_index_backfill_enter_the_collection_write_gate()
    {
        string[] allowed =
        [
            "QuickBuilder.Cms.Infrastructure.CollectionWriteGate",
            "QuickBuilder.Cms.Infrastructure.ItemWrites",
            "QuickBuilder.Cms.Infrastructure.ItemIndexBackfillService",
            "QuickBuilder.Cms.CmsModule",
        ];

        var dependents = Types.InAssembly(Cms)
            .That().HaveDependencyOn("QuickBuilder.Cms.Infrastructure.CollectionWriteGate")
            .GetTypes()
            .Select(t => t.FullName!)
            .Select(name => name.Split('+')[0])
            .Where(name => !name.Contains('<', StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.All(dependents, name => Assert.Contains(name, allowed));
    }
}
