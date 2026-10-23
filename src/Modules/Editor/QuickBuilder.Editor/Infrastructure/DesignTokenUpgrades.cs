using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Shared.Infrastructure.Schemas;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// The upgrade chain of design token sets, and the reads that use it
/// (019 contracts/schema-upgrades.md §1, §3).
/// </summary>
/// <remarks>
/// Empty today: the contract is at version 1. A registered, replaceable singleton so tests can
/// give it a step that fails.
/// </remarks>
internal sealed class DesignTokenUpgrades(SchemaUpgradeChain chain, ILogger<DesignTokenUpgrades> logger)
{
    public const string Contract = "design-tokens";

    public SchemaUpgradeChain Chain { get; } = chain;

    public static SchemaUpgradeChain CreateChain() => new(Contract, DesignTokens.CurrentVersion, []);

    /// <summary>For the admin and for publishing: the current document, or the refusal to give.</summary>
    public Result<JsonElement> ForEditing(string subjectKind, Guid subjectId, string storedDocument, int storedVersion)
    {
        using var parsed = JsonDocument.Parse(storedDocument);
        var read = Read(subjectKind, subjectId, parsed.RootElement, storedVersion);
        return read switch
        {
            { Usable: { } current } => Result.Success(current.Clone()),
            SchemaRead.TooNew => Result.Failure<JsonElement>(EditorErrors.DocumentTooNew),
            _ => Result.Failure<JsonElement>(EditorErrors.DocumentUpgradeFailed),
        };
    }

    /// <summary>
    /// For the public path: the current document, or null when it cannot be used. The caller
    /// falls back to the default tokens: a site-wide document must not make every page fail.
    /// </summary>
    public JsonElement? ForRendering(Guid siteId, string storedDocument, int storedVersion)
    {
        using var parsed = JsonDocument.Parse(storedDocument);
        return Read("design", siteId, parsed.RootElement, storedVersion).Usable is { } current ? current.Clone() : null;
    }

    private SchemaRead Read(string subjectKind, Guid subjectId, JsonElement document, int storedVersion)
    {
        var read = Chain.Read(document, storedVersion);
        SchemaReadLog.Report(logger, read, Contract, subjectKind, subjectId);
        return read;
    }
}
