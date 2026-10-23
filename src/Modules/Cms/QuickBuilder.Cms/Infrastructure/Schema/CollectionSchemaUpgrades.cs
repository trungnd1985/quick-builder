using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Shared.Infrastructure.Schemas;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure.Schema;

/// <summary>
/// The upgrade chain of the collection schema contract, which versions both a collection's
/// schema and its items' values (019 contracts/schema-upgrades.md §1, §3).
/// </summary>
/// <remarks>
/// Both documents carry <c>schemaVersion</c> in their JSON, which is what the chain reads.
/// Empty today: the contract is at version 1. A registered, replaceable singleton so tests can
/// give the chain a step that fails.
/// </remarks>
internal sealed class CollectionSchemaUpgrades(SchemaUpgradeChain chain, ILogger<CollectionSchemaUpgrades> logger)
{
    public const string Contract = "collection-schema";

    public SchemaUpgradeChain Chain { get; } = chain;

    public static SchemaUpgradeChain CreateChain() => new(Contract, CollectionSchema.CurrentVersion, []);

    /// <summary>The document at the current version, as read; logs one that cannot be used.</summary>
    public SchemaRead Read(string subjectKind, Guid subjectId, JsonElement document)
    {
        var read = Chain.Read(document, StoredVersion(document));
        SchemaReadLog.Report(logger, read, Contract, subjectKind, subjectId);
        return read;
    }

    /// <summary>For the public path: the current document, or null when it cannot be used.</summary>
    public JsonElement? ForRendering(string subjectKind, Guid subjectId, JsonElement document) =>
        Read(subjectKind, subjectId, document).Usable;

    /// <summary>
    /// For the admin: the upgraded document when it was older (null when it is already current,
    /// so the caller keeps the stored form as it is), or the refusal to give.
    /// </summary>
    public Result<JsonElement?> UpgradedForEditing(string subjectKind, Guid subjectId, string storedDocument)
    {
        using var parsed = JsonDocument.Parse(storedDocument);
        return Read(subjectKind, subjectId, parsed.RootElement) switch
        {
            SchemaRead.Current => Result.Success<JsonElement?>(null),
            SchemaRead.Upgraded upgraded => Result.Success<JsonElement?>(upgraded.Document.Clone()),
            SchemaRead.TooNew => Result.Failure<JsonElement?>(CmsErrors.DocumentTooNew),
            _ => Result.Failure<JsonElement?>(CmsErrors.DocumentUpgradeFailed),
        };
    }

    /// <summary>The version a document says it is; 0 (which no chain accepts) when it says none.</summary>
    private static int StoredVersion(JsonElement document) =>
        document.ValueKind == JsonValueKind.Object
        && document.TryGetProperty("schemaVersion", out var version)
        && version.TryGetInt32(out var number)
            ? number
            : 0;
}
