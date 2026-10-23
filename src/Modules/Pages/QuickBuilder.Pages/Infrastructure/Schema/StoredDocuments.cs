using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.Schemas;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure.Schema;

/// <summary>
/// Reads a stored page, component or menu document at the current version before it leaves
/// the module (019 contracts/schema-upgrades.md §3, FR-021, FR-025, FR-026).
/// </summary>
/// <remarks>
/// The one place Pages decides what an old, too-new or broken document means, so the public
/// path, the admin and publishing agree. Reading never writes: the result is stored only when
/// a member saves or publishes (FR-022, FR-023a).
/// </remarks>
internal sealed class StoredDocuments(PageSchemaUpgrades pages, MenuSchemaUpgrades menus, ILogger<StoredDocuments> logger)
{
    /// <summary>The version a document of <paramref name="subjectKind"/> is stored at today.</summary>
    public static int CurrentVersion(string subjectKind) =>
        subjectKind == SubjectKinds.Menu ? MenuSchema.CurrentVersion : PageSchema.CurrentVersion;

    /// <summary>Reads through the subject's chain, logging a document that cannot be used.</summary>
    public SchemaRead Read(string subjectKind, Guid subjectId, JsonElement document, int storedVersion)
    {
        var read = subjectKind == SubjectKinds.Menu
            ? menus.Read(document, storedVersion)
            : pages.Read(document, storedVersion);

        SchemaReadLog.Report(logger, read, subjectKind == SubjectKinds.Menu ? "menu" : "page-schema", subjectKind, subjectId);
        return read;
    }

    /// <summary>
    /// For the public path: the version and document to hand over. A document that cannot be
    /// used is handed over empty, at a version no renderer accepts, so its page renders nothing
    /// as an unrenderable page does (012) and every other page renders. Empty rather than the
    /// stored text: the renderer reads the version inside the document, and an old document
    /// that failed to upgrade still says it is an old, renderable one.
    /// </summary>
    public (int Version, JsonElement Document) ForRendering(string subjectKind, Guid subjectId, JsonElement document, int storedVersion) =>
        Read(subjectKind, subjectId, document, storedVersion) switch
        {
            { Usable: { } current } => (CurrentVersion(subjectKind), current),
            SchemaRead.TooNew => (storedVersion, Empty),
            _ => (Unrenderable, Empty),
        };

    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>For the admin and for publishing: the current document, or the refusal to give.</summary>
    public Result<JsonElement> ForEditing(string subjectKind, Guid subjectId, string storedDocument, int storedVersion)
    {
        using var parsed = JsonDocument.Parse(storedDocument);
        return Read(subjectKind, subjectId, parsed.RootElement, storedVersion) switch
        {
            { Usable: { } current } => Result.Success(current.Clone()),
            SchemaRead.TooNew => Result.Failure<JsonElement>(PagesErrors.DocumentTooNew),
            _ => Result.Failure<JsonElement>(PagesErrors.DocumentUpgradeFailed),
        };
    }

    /// <summary>A version no renderer lists: what a document that failed to upgrade is handed over as.</summary>
    internal const int Unrenderable = 0;
}
