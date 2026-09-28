namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// The page schema contract: which versions exist and the limits every content document
/// obeys (docs/contracts/page-schema.md, research P-09 and P-10).
/// </summary>
/// <remarks>
/// The schema itself is <c>docs/contracts/page-schema/v1.schema.json</c>, embedded into this
/// assembly from where it lives rather than copied, so the file the contract document
/// describes is the file the product validates against.
/// </remarks>
public static class PageSchema
{
    /// <summary>The version new documents are written in.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The most bytes of UTF-8 JSON one document may take.</summary>
    public const int MaxDocumentBytes = 1_048_576;

    /// <summary>The most nodes one document may hold, the root included.</summary>
    public const int MaxNodes = 5000;

    /// <summary>The deepest a node may sit. The root is at depth 1.</summary>
    public const int MaxNodeDepth = 32;

    /// <summary>The longest node id, in characters.</summary>
    public const int MaxNodeIdLength = 64;

    /// <summary>
    /// The most characters of text, in code points, one Rich Text block may hold across all
    /// its text nodes (contract §3 rule 9, 011 FR-013).
    /// </summary>
    public const int MaxRichTextChars = 20_000;

    private const string ResourcePrefix = "QuickBuilder.Pages.Contracts.PageSchema.v";

    /// <summary>Every version the product can validate and store.</summary>
    public static IReadOnlySet<int> SupportedVersions { get; } = new HashSet<int> { 1 };

    /// <summary>The embedded schema file for <paramref name="version"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The version is not supported.</exception>
    public static Stream OpenSchema(int version)
    {
        if (!SupportedVersions.Contains(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "No page schema exists for this version.");
        }

        return typeof(PageSchema).Assembly.GetManifestResourceStream($"{ResourcePrefix}{version}.schema.json")
            ?? throw new InvalidOperationException(
                $"The page schema v{version} is not embedded. Check the EmbeddedResource in QuickBuilder.Pages.Contracts.csproj.");
    }
}
