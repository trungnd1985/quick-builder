namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// The menu document contract: which versions exist and the limits every menu obeys
/// (docs/contracts/menu.md, 024 research NV-03, NV-20).
/// </summary>
/// <remarks>
/// The schema itself is <c>docs/contracts/menu/v1.schema.json</c>, embedded into this assembly
/// from where it lives, as <see cref="PageSchema"/> embeds the page schema, so the file the
/// contract document describes is the file the product validates against.
/// </remarks>
public static class MenuSchema
{
    /// <summary>The version new menus are written in.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The most bytes of UTF-8 JSON one menu document may take (menu.md rule 0).</summary>
    public const int MaxDocumentBytes = 262_144;

    /// <summary>The most entries one menu may hold, at every level together (rule 4).</summary>
    public const int MaxEntries = 300;

    /// <summary>The deepest an entry may sit; top-level entries are level 1 (rule 5).</summary>
    public const int MaxLevels = 3;

    /// <summary>The longest label, in characters.</summary>
    public const int MaxLabelLength = 100;

    private const string ResourcePrefix = "QuickBuilder.Pages.Contracts.MenuSchema.v";

    /// <summary>Every version the product can validate, store and render.</summary>
    public static IReadOnlySet<int> SupportedVersions { get; } = new HashSet<int> { 1 };

    /// <summary>A new menu's draft: no entries.</summary>
    public static string EmptyDocument => """{"schemaVersion":1,"entries":[]}""";

    /// <summary>The embedded schema file for <paramref name="version"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The version is not supported.</exception>
    public static Stream OpenSchema(int version)
    {
        if (!SupportedVersions.Contains(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "No menu schema exists for this version.");
        }

        return typeof(MenuSchema).Assembly.GetManifestResourceStream($"{ResourcePrefix}{version}.schema.json")
            ?? throw new InvalidOperationException(
                $"The menu schema v{version} is not embedded. Check the EmbeddedResource in QuickBuilder.Pages.Contracts.csproj.");
    }
}
