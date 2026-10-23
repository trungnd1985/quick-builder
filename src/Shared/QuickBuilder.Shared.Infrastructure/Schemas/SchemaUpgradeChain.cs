using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Shared.Infrastructure.Schemas;

/// <summary>
/// Every upgrade step of one versioned contract, from version 1 to the current one
/// (019 research RL-19, contracts/schema-upgrades.md §2; constitution 2.5.1 Principle VI).
/// </summary>
/// <remarks>
/// Owning modules read every stored document through their contract's chain before it leaves
/// the module, so Rendering and the admin only ever see the current version. Reading never
/// writes: a document is stored at the current version on its next save or publish, and is
/// never rewritten in bulk (clarification Q4).
/// </remarks>
public sealed class SchemaUpgradeChain
{
    private readonly ISchemaUpgradeStep[] _steps;

    /// <param name="contract">The contract's folder under <c>docs/contracts/</c>, e.g. <c>page-schema</c>.</param>
    /// <param name="current">The version this release writes.</param>
    /// <param name="steps">One step from each version 1 … current-1; refused otherwise.</param>
    /// <param name="versionProperty">The property that carries the version in a document.</param>
    public SchemaUpgradeChain(string contract, int current, IEnumerable<ISchemaUpgradeStep> steps, string versionProperty = "schemaVersion")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        ArgumentOutOfRangeException.ThrowIfLessThan(current, 1);
        ArgumentNullException.ThrowIfNull(steps);

        var ordered = steps.OrderBy(s => s.From).ToArray();
        var expected = Enumerable.Range(1, current - 1).ToArray();
        if (!ordered.Select(s => s.From).SequenceEqual(expected))
        {
            throw new ArgumentException(
                $"The {contract} chain at version {current} needs exactly one step from each of versions " +
                $"[{string.Join(", ", expected)}]; it has steps from [{string.Join(", ", ordered.Select(s => s.From))}].",
                nameof(steps));
        }

        Contract = contract;
        Current = current;
        VersionProperty = versionProperty;
        _steps = ordered;
    }

    public string Contract { get; }

    public int Current { get; }

    public string VersionProperty { get; }

    public IReadOnlyList<ISchemaUpgradeStep> Steps => _steps;

    /// <summary>
    /// The document at the current version, or why there is none. Never changes
    /// <paramref name="document"/>.
    /// </summary>
    /// <param name="storedVersion">The version stored with the document (its column).</param>
    public SchemaRead Read(JsonElement document, int storedVersion)
    {
        if (storedVersion > Current)
        {
            return new SchemaRead.TooNew(storedVersion);
        }

        if (storedVersion < 1)
        {
            return new SchemaRead.Failed(storedVersion, $"{Contract} has no version {storedVersion}.");
        }

        if (storedVersion == Current)
        {
            return new SchemaRead.Current(document);
        }

        if (JsonNode.Parse(document.GetRawText()) is not JsonObject working)
        {
            return new SchemaRead.Failed(storedVersion, $"A {Contract} document is not a JSON object.");
        }

        foreach (var step in _steps.Where(s => s.From >= storedVersion))
        {
            try
            {
                working = step.Upgrade(working);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The message of a step's exception may quote content; only its type is kept.
                return new SchemaRead.Failed(storedVersion, $"The {Contract} step from {step.From} to {step.From + 1} threw {ex.GetType().Name}.");
            }

            if (working[VersionProperty]?.GetValueKind() != JsonValueKind.Number
                || working[VersionProperty]!.GetValue<int>() != step.From + 1)
            {
                return new SchemaRead.Failed(storedVersion, $"The {Contract} step from {step.From} did not set {VersionProperty} to {step.From + 1}.");
            }
        }

        using var upgraded = JsonDocument.Parse(working.ToJsonString());
        return new SchemaRead.Upgraded(upgraded.RootElement.Clone(), storedVersion);
    }
}
