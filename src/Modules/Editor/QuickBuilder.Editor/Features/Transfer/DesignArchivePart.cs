using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Editor.Features.Transfer;

/// <summary>
/// The Editor module's share of an archive: the site's design token draft, its live version
/// and every retained version (020 contracts/archive-parts.md §3; FR-001, FR-002).
/// </summary>
/// <remarks>
/// Editing sessions and takeover records are not carried (023; FR-003). Documents are written
/// as stored, with their own <c>schemaVersion</c>, and upgraded on read after an import (ST-21).
/// </remarks>
internal sealed partial class DesignArchivePart(EditorDbContext database, IServiceProvider services) : ISiteArchivePart
{
    public const string PartName = "design";

    public string Name => PartName;

    public int Order => 30;

    public int Version => 1;

    public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>
    {
        [DesignTokenUpgrades.Contract] = DesignTokens.CurrentVersion,
    };

    public async Task ExportAsync(PartExportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);

        var draft = await database.DesignTokenSets.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var publication = await database.DesignPublications.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // A site whose design was never edited has neither: it renders with the defaults.
        if (draft is not null || publication is not null)
        {
            context.Authors.Add(draft?.UpdatedBy);
            context.Authors.Add(publication?.LiveBy);
            await context.Archive.WriteLinesAsync(PartName, "design", One(new DesignRow(
                draft?.SchemaVersion,
                draft is null ? null : JsonElement.Parse(draft.Document),
                draft?.UpdatedAtUtc,
                draft?.UpdatedBy,
                publication?.LiveNumber,
                publication?.NextNumber,
                publication?.LiveSinceUtc,
                publication?.LiveBy)), cancellationToken).ConfigureAwait(false);
        }

        var versions = await context.Archive.WriteLinesAsync(PartName, "design-versions", Versions(context, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        context.Count("design_versions", versions);
    }

    private async IAsyncEnumerable<DesignVersionRow> Versions(PartExportContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var v in database.DesignVersions.AsNoTracking().OrderBy(v => v.Number).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            context.Authors.Add(v.PublishedBy);
            yield return new DesignVersionRow(
                v.Number,
                v.SchemaVersion,
                JsonElement.Parse(v.Document),
                v.Kind,
                v.RestoredFromNumber,
                v.PublishedBy,
                v.PublishedAtUtc);
        }
    }

    private static async IAsyncEnumerable<T> One<T>(T row)
    {
        yield return row;
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary><c>parts/design/design.jsonl</c>: the site's token draft and which version is live (one line).</summary>
internal sealed record DesignRow(
    int? DraftSchemaVersion,
    JsonElement? Draft,
    DateTimeOffset? DraftUpdatedAtUtc,
    Guid? DraftUpdatedBy,
    int? LiveNumber,
    int? NextNumber,
    DateTimeOffset? LiveSinceUtc,
    Guid? LiveBy);

/// <summary><c>parts/design/design-versions.jsonl</c>: one retained published version.</summary>
internal sealed record DesignVersionRow(
    int Number,
    int SchemaVersion,
    JsonElement Document,
    string Kind,
    int? RestoredFromNumber,
    Guid PublishedBy,
    DateTimeOffset PublishedAtUtc);
