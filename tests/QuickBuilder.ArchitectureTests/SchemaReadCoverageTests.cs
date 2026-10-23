using System.Reflection;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Every place a module hands a stored document to anyone else reads it through its contract's
/// upgrade chain (019 contracts/schema-upgrades.md §3, research RL-19).
/// </summary>
/// <remarks>
/// The read paths are named here on purpose: a renamed or new one fails this test until it is
/// added, and a listed one that stops taking its chain fails it too.
/// </remarks>
public sealed class SchemaReadCoverageTests
{
    public static TheoryData<string, string> ReadPaths() => new()
    {
        // Pages: live documents to Rendering and Forms; drafts, versions and menus to the admin;
        // the source of a rollback or a copy to the draft.
        { "QuickBuilder.Pages.Infrastructure.PublishedContentReader", "QuickBuilder.Pages.Infrastructure.Schema.StoredDocuments" },
        { "QuickBuilder.Pages.Infrastructure.PageReads", "QuickBuilder.Pages.Infrastructure.Schema.StoredDocuments" },
        { "QuickBuilder.Pages.Infrastructure.MenuReads", "QuickBuilder.Pages.Infrastructure.Schema.StoredDocuments" },
        { "QuickBuilder.Pages.Infrastructure.PublishingWrites", "QuickBuilder.Pages.Infrastructure.Schema.StoredDocuments" },
        { "QuickBuilder.Pages.Features.PublishingHistory.GetVersionHandler", "QuickBuilder.Pages.Infrastructure.Schema.StoredDocuments" },
        { "QuickBuilder.Pages.Features.PublishingHistory.CopyVersionToDraftHandler", "QuickBuilder.Pages.Infrastructure.Schema.StoredDocuments" },

        // Editor: the token set for editing and publishing, the published design for rendering.
        { "QuickBuilder.Editor.Infrastructure.DesignTokenReader", "QuickBuilder.Editor.Infrastructure.DesignTokenUpgrades" },
        { "QuickBuilder.Editor.Infrastructure.PublishedDesignReader", "QuickBuilder.Editor.Infrastructure.DesignTokenUpgrades" },
        { "QuickBuilder.Editor.Infrastructure.DesignPublishing", "QuickBuilder.Editor.Infrastructure.DesignTokenUpgrades" },

        // Cms: schemas and published item values to Rendering and Pages; collections and items
        // to the admin.
        { "QuickBuilder.Cms.Infrastructure.PublishedItemReader", "QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades" },
        { "QuickBuilder.Cms.Infrastructure.CollectionSchemaReader", "QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades" },
        { "QuickBuilder.Cms.Infrastructure.CollectionReads", "QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades" },
        { "QuickBuilder.Cms.Infrastructure.ItemReads", "QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades" },
    };

    [Theory]
    [MemberData(nameof(ReadPaths))]
    public void A_read_path_takes_its_contracts_upgrade_chain(string readPath, string chain)
    {
        var type = ModuleBoundaryTests.ModuleImplementations
            .Select(a => a.GetType(readPath))
            .SingleOrDefault(t => t is not null);

        Assert.True(type is not null, $"{readPath} no longer exists; list its replacement here (019 schema-upgrades.md §3).");
        var takes = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.FullName);

        Assert.Contains(chain, takes);
    }
}
