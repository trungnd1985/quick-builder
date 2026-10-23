using QuickBuilder.Pages.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Rendering.Rendering.Blocks;
using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// Every node type of the page schema has exactly one renderer, and no renderer invents a type
/// (012 FR-023, research PR-09; constitution Principle VI).
/// </summary>
public sealed class BlockRendererCoverageTests
{
    /// <summary>Every renderer the assembly defines, as the module registers them.</summary>
    internal static IReadOnlyList<IBlockRenderer> AllRenderers() =>
    [
        .. typeof(IBlockRenderer).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBlockRenderer).IsAssignableFrom(t))
            .Select(t => (IBlockRenderer)Activator.CreateInstance(t, nonPublic: true)!),
    ];

    [Fact]
    public void Every_schema_node_type_has_exactly_one_renderer()
    {
        // The document root is the page itself, not a block: the page renderer walks it.
        var schemaTypes = BlockDefaults.Current.Types.Where(t => t != "root").Order(StringComparer.Ordinal).ToList();
        var rendererTypes = AllRenderers().Select(r => r.Type).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(schemaTypes, rendererTypes);
    }

    /// <remarks>
    /// 019 RL-19: the renderer renders the current version, and every older version that exists
    /// reaches it through a step of Pages' upgrade chain (constitution Principle VI: every
    /// version still present must render).
    /// </remarks>
    [Fact]
    public void The_renderer_renders_every_schema_version_that_exists()
    {
        var versions = Directory.GetFiles(RepositoryFiles.Path("docs", "contracts", "page-schema"), "v*.schema.json")
            .Select(f => int.Parse(Path.GetFileName(f)[1..].Split('.')[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var steps = QuickBuilder.Pages.Infrastructure.Schema.PageSchemaUpgrades.Create().Pages.Steps.Select(s => s.From).ToList();

        Assert.NotEmpty(versions);
        Assert.Equal([PageSchema.CurrentVersion], PageRenderer.SupportedSchemaVersions);
        Assert.All(versions, v => Assert.True(v == PageSchema.CurrentVersion || steps.Contains(v), $"Page schema version {v} neither renders nor upgrades."));
        Assert.All(PageSchema.SupportedVersions, v => Assert.True(v == PageSchema.CurrentVersion || steps.Contains(v)));
    }

    [Fact]
    public void The_navigation_renderer_reads_every_menu_schema_version_that_exists()
    {
        // 024 tasks T075: a new menu schema version fails the build until the renderer lists it.
        var versions = Directory.GetFiles(RepositoryFiles.Path("docs", "contracts", "menu"), "v*.schema.json")
            .Select(f => int.Parse(Path.GetFileName(f)[1..].Split('.')[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        var steps = QuickBuilder.Pages.Infrastructure.Schema.MenuSchemaUpgrades.Create().Menus.Steps.Select(s => s.From).ToList();

        Assert.NotEmpty(versions);
        Assert.Equal([MenuSchema.CurrentVersion], MenuModel.SupportedSchemaVersions);
        Assert.All(versions, v => Assert.True(v == MenuSchema.CurrentVersion || steps.Contains(v), $"Menu schema version {v} neither renders nor upgrades."));
    }
}
