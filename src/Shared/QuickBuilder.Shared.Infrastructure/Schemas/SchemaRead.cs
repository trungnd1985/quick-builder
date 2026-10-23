using System.Text.Json;

namespace QuickBuilder.Shared.Infrastructure.Schemas;

/// <summary>
/// What reading a stored document through its contract's upgrade chain gave
/// (019 contracts/schema-upgrades.md §2).
/// </summary>
public abstract record SchemaRead
{
    private SchemaRead()
    {
    }

    /// <summary>The document was stored at the current version.</summary>
    public sealed record Current(JsonElement Document) : SchemaRead;

    /// <summary>The document was older and is now at the current version, in memory only.</summary>
    public sealed record Upgraded(JsonElement Document, int FromVersion) : SchemaRead;

    /// <summary>
    /// The document is newer than this release knows (FR-026). It must not be rendered, edited
    /// or written; the startup check and the patch rule should have made this impossible.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "\"Too new\" is the state, not a newer copy of a type.")]
    public sealed record TooNew(int StoredVersion) : SchemaRead;

    /// <summary>A step failed for this document (FR-025). The reason names no content.</summary>
    public sealed record Failed(int StoredVersion, string Reason) : SchemaRead;

    /// <summary>The current-version document, or null when there is none to use.</summary>
    public JsonElement? Usable => this switch
    {
        Current current => current.Document,
        Upgraded upgraded => upgraded.Document,
        _ => null,
    };
}
