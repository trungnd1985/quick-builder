using NetArchTest.Rules;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Only the code that must see trashed rows uses <c>TrashQuery</c> (021 TR-03).
/// </summary>
/// <remarks>
/// The trash filter is what makes trashed content behave as deleted everywhere without each
/// feature having to remember (FR-006). Every use of <c>TrashQuery.IncludingTrash</c> or
/// <c>OnlyTrash</c> is a place where trashed content can reach a caller, so each one is
/// listed here with the decision that allows it. A new caller fails this test until it is
/// added on purpose.
/// </remarks>
public sealed class TrashQueryUsageTests
{
    private const string TrashQuery = "QuickBuilder.Shared.Infrastructure.Trash.TrashQuery";

    /// <summary>Namespaces whose every type may read trashed rows.</summary>
    private static readonly string[] AllowedNamespaces =
    [
        ".Features.Trash",        // the trash slices of Pages and Media (TR-01)
        ".Features.Items.Trash",  // the trash slices of Cms (TR-01)
        ".Features.Transfer",     // site import clears and matches trashed rows (TR-17)
    ];

    /// <summary>Single types that may read trashed rows, by simple name.</summary>
    private static readonly string[] AllowedTypes =
    [
        "PagesTrashPurge",                // TR-14: the permanent delete of pages and components
        "CmsTrashPurge",                  // TR-14
        "MediaTrashPurge",                // TR-14
        "PagesTrashPurgeService",         // TR-13
        "CmsTrashPurgeService",           // TR-13
        "MediaTrashPurgeService",         // TR-13
        "MediaOrphanSweepService",        // TR-18: never delete a trashed file's bytes
        "ThumbnailWorker",                // TR-18: variants of trashed files are kept
        "ImageMetadataBackfillService",   // TR-18
        "SearchTextBackfillService",      // TR-18
        "ItemIndexBackfillService",       // TR-18
        "MediaUsageReader",               // TR-06: "used by content in the trash"
        "ItemMediaUsageReader",           // TR-06
        "CollectionReads",                // FR-023: the confirmation counts trashed items
        "MenuTargetStates",               // FR-027: a trashed menu target shows as in the trash
        "CollectionSchemaReader",         // FR-027: which menu targets are items in the trash
        "PublishingWrites",               // spec edge case: a rollback refusal names a trashed component
        "ItemWrites",                     // TR-10: a restore goes through the one item write path
    ];

    [Fact]
    public void Only_listed_callers_read_trashed_rows()
    {
        var offenders = ModuleBoundaryTests.ModuleImplementations
            .SelectMany(assembly => Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("Coverlet")
                .ShouldNot()
                .HaveDependencyOn(TrashQuery)
                .GetResult()
                .FailingTypes ?? [])
            .Where(type => !IsAllowed(type))
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Only the callers listed in TrashQueryUsageTests may read trashed rows (021 TR-03). " +
            "Add a new caller there with the decision that allows it:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>A listed type, a type in a listed namespace, or a compiler type nested in one.</summary>
    private static bool IsAllowed(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (AllowedTypes.Contains(current.Name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        var ns = type.Namespace ?? string.Empty;
        return AllowedNamespaces.Any(allowed => ns.EndsWith(allowed, StringComparison.Ordinal) || ns.Contains(allowed + ".", StringComparison.Ordinal));
    }
}
