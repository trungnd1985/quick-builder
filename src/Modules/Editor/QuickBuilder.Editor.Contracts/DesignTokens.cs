namespace QuickBuilder.Editor.Contracts;

/// <summary>
/// The design token contract: which versions exist and the limits every token set obeys
/// (docs/contracts/design-tokens.md, research ED-14).
/// </summary>
/// <remarks>
/// The schema itself is <c>docs/contracts/design-tokens/v1.schema.json</c>, embedded into
/// this assembly from where it lives rather than copied, as the page schema is.
/// <c>DesignTokenContractTests</c> fails the build when the values here, the document and the
/// schema disagree.
/// </remarks>
public static class DesignTokens
{
    /// <summary>The version new token sets are written in.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The most bytes of UTF-8 JSON one token set may take.</summary>
    public const int MaxDocumentBytes = 65_536;

    /// <summary>The grammar of a token id. Ids are unique across all kinds within a set.</summary>
    public const string TokenIdPattern = "^[a-z0-9-]{1,40}$";

    /// <summary>The most colours one set may hold.</summary>
    public const int MaxColors = 24;

    /// <summary>The most text-size steps one set may hold.</summary>
    public const int MaxSizes = 12;

    /// <summary>The most spacing steps one set may hold.</summary>
    public const int MaxSpacing = 12;

    private const string ResourcePrefix = "QuickBuilder.Editor.Contracts.DesignTokens.v";

    /// <summary>Every version the product can validate and store.</summary>
    public static IReadOnlySet<int> SupportedVersions { get; } = new HashSet<int> { 1 };

    /// <summary>The embedded schema file for <paramref name="version"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The version is not supported.</exception>
    public static Stream OpenSchema(int version)
    {
        if (!SupportedVersions.Contains(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "No design token schema exists for this version.");
        }

        return typeof(DesignTokens).Assembly.GetManifestResourceStream($"{ResourcePrefix}{version}.schema.json")
            ?? throw new InvalidOperationException(
                $"The design token schema v{version} is not embedded. Check the EmbeddedResource in QuickBuilder.Editor.Contracts.csproj.");
    }
}
