using System.Text.Json.Nodes;

namespace QuickBuilder.Shared.Infrastructure.Schemas;

/// <summary>
/// Upgrades a stored document of one contract from version <see cref="From"/> to the next
/// (019 contracts/schema-upgrades.md §2, constitution Principle VI).
/// </summary>
/// <remarks>
/// Pure: no I/O, no clock, no services. The step receives a copy it may change and must set
/// the document's version to <c>From + 1</c>. Every step has golden files under
/// <c>docs/contracts/&lt;contract&gt;/upgrades/v&lt;From&gt;-to-v&lt;From+1&gt;/</c>.
/// </remarks>
public interface ISchemaUpgradeStep
{
    /// <summary>The version this step upgrades from.</summary>
    int From { get; }

    JsonObject Upgrade(JsonObject document);
}
