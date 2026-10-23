using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Schemas;

namespace QuickBuilder.Pages.Infrastructure.Schema;

/// <summary>
/// The upgrade chains of the documents Pages stores for pages and components: the page schema,
/// and the binding expressions inside it, which carry their own version
/// (019 contracts/schema-upgrades.md §1).
/// </summary>
/// <remarks>
/// Empty today: both contracts are at version 1. Raising either adds a step here, with golden
/// files (docs/contracts/&lt;contract&gt;/upgrades/), and relaxes the drafts' version check
/// (schema-upgrades.md §5). Registered as a replaceable singleton so tests can give it a step
/// that fails.
/// </remarks>
internal sealed class PageSchemaUpgrades(SchemaUpgradeChain pages, SchemaUpgradeChain bindings)
{
    public const string BindingVersionProperty = "bindingVersion";

    public SchemaUpgradeChain Pages { get; } = pages;

    public SchemaUpgradeChain Bindings { get; } = bindings;

    public static PageSchemaUpgrades Create() => new(
        new SchemaUpgradeChain("page-schema", PageSchema.CurrentVersion, []),
        new SchemaUpgradeChain("binding-expression", PageSchema.CurrentBindingVersion, [], BindingVersionProperty));

    /// <summary>
    /// The document at the current page schema and binding versions. The page chain runs first;
    /// a document that carries bindings then runs through the binding chain.
    /// </summary>
    public SchemaRead Read(JsonElement document, int storedVersion)
    {
        var page = Pages.Read(document, storedVersion);
        if (page.Usable is not { } current
            || !current.TryGetProperty(BindingVersionProperty, out var binding)
            || binding.ValueKind != JsonValueKind.Number
            || !binding.TryGetInt32(out var bindingVersion))
        {
            return page;
        }

        return Bindings.Read(current, bindingVersion) switch
        {
            SchemaRead.Current => page,
            SchemaRead.Upgraded upgraded => new SchemaRead.Upgraded(upgraded.Document, storedVersion),
            var unusable => unusable,
        };
    }
}

/// <summary>The upgrade chain of menu documents (024; 019 contracts/schema-upgrades.md §1).</summary>
internal sealed class MenuSchemaUpgrades(SchemaUpgradeChain menus)
{
    public SchemaUpgradeChain Menus { get; } = menus;

    public static MenuSchemaUpgrades Create() => new(new SchemaUpgradeChain("menu", MenuSchema.CurrentVersion, []));

    public SchemaRead Read(JsonElement document, int storedVersion) => Menus.Read(document, storedVersion);
}
