namespace QuickBuilder.Cms.Contracts;

/// <summary>
/// The collection schema contract: which versions exist and where the definition is
/// (docs/contracts/collection-schema.md, research CS-02 … CS-04).
/// </summary>
/// <remarks>
/// The schema itself is <c>docs/contracts/collection-schema/v1.schema.json</c>, embedded into
/// this assembly from where it lives rather than copied, so the file the contract document
/// describes is the file the product validates against. Its root is a collection's schema
/// document; its <c>$defs</c> hold the item values document and one value per field type.
/// </remarks>
public static class CollectionSchema
{
    /// <summary>The version new schemas and item values are written in.</summary>
    public const int CurrentVersion = 1;

    private const string ResourcePrefix = "QuickBuilder.Cms.Contracts.CollectionSchema.v";

    /// <summary>Every version the product can validate and store.</summary>
    public static IReadOnlySet<int> SupportedVersions { get; } = new HashSet<int> { 1 };

    /// <summary>The embedded schema file for <paramref name="version"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The version is not supported.</exception>
    public static Stream OpenSchema(int version)
    {
        if (!SupportedVersions.Contains(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "No collection schema exists for this version.");
        }

        return typeof(CollectionSchema).Assembly.GetManifestResourceStream($"{ResourcePrefix}{version}.schema.json")
            ?? throw new InvalidOperationException(
                $"The collection schema v{version} is not embedded. Check the EmbeddedResource in QuickBuilder.Cms.Contracts.csproj.");
    }
}
