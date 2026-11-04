using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Pages.Features.Transfer;

/// <summary>
/// The Pages module's share of an archive: the page tree, pages, shared components and menus
/// with their drafts, publications, retained versions and pending schedules, and redirects
/// (020 contracts/archive-parts.md §3; FR-001, FR-002; 017 FR-040, 018 FR-040, 024 FR-036).
/// </summary>
/// <remarks>
/// Pages, components and menus share one publishing model (012, 024 NV-01), so their
/// publications, versions and schedules are three entries keyed by subject kind. Forms travel
/// inside the documents (018 FR-040). Usage rows, live dependencies, menu targets and usages,
/// the sitemap state and the publish, redirect and menu change logs are not carried: the first
/// are rebuilt from the documents, the logs record this installation's actions (research ST-03).
/// </remarks>
internal sealed partial class PagesArchivePart(PagesDbContext database, IServiceProvider services) : ISiteArchivePart
{
    public const string PartName = "pages";

    public string Name => PartName;

    public int Order => 50;

    public int Version => 1;

    public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>
    {
        ["page-schema"] = PageSchema.CurrentVersion,
        ["binding-expression"] = PageSchema.CurrentBindingVersion,
        ["menu"] = MenuSchema.CurrentVersion,
    };

    public async Task ExportAsync(PartExportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        var authors = context.Authors;

        context.Count(ArchiveKinds.Page, await context.Archive.WriteLinesAsync(PartName, "tree",
            Stream(database.Pages.OrderBy(p => p.Depth).ThenBy(p => p.ParentId).ThenBy(p => p.Position), p =>
            {
                authors.Add(p.CreatedBy);
                authors.Add(p.UpdatedBy);
                authors.Add(p.CopiedBy);
                return new PageRow(
                    p.Id, p.ParentId, p.Position, p.Title, p.Slug, p.IsHome, p.TemplateCollectionId,
                    p.CopiedFromId, p.CopiedFromSavedAtUtc, p.CopiedBy, p.CopiedAtUtc,
                    p.CreatedAtUtc, p.CreatedBy, p.UpdatedAtUtc, p.UpdatedBy);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));
        context.Progress.Report(0.1);

        await context.Archive.WriteLinesAsync(PartName, "page-drafts",
            Stream(database.PageDrafts.OrderBy(d => d.PageId), d =>
            {
                authors.Add(d.UpdatedBy);
                return new DraftRow(d.PageId, d.SchemaVersion, Parse(d.Document), d.UpdatedAtUtc, d.UpdatedBy);
            }, cancellationToken), cancellationToken).ConfigureAwait(false);
        context.Progress.Report(0.3);

        var componentDrafts = await database.ComponentDrafts.AsNoTracking()
            .ToDictionaryAsync(d => d.ComponentId, cancellationToken)
            .ConfigureAwait(false);
        context.Count(ArchiveKinds.Component, await context.Archive.WriteLinesAsync(PartName, "components",
            Stream(database.Components.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id), c =>
            {
                var draft = componentDrafts.GetValueOrDefault(c.Id);
                authors.Add(c.CreatedBy);
                authors.Add(c.UpdatedBy);
                authors.Add(c.CopiedBy);
                authors.Add(draft?.UpdatedBy);
                return new ComponentRow(
                    c.Id, c.Name, c.CopiedFromId, c.CopiedFromSavedAtUtc, c.CopiedBy, c.CopiedAtUtc,
                    c.CreatedAtUtc, c.CreatedBy, c.UpdatedAtUtc, c.UpdatedBy,
                    draft is null ? null : new DraftRow(c.Id, draft.SchemaVersion, Parse(draft.Document), draft.UpdatedAtUtc, draft.UpdatedBy));
            }, cancellationToken), cancellationToken).ConfigureAwait(false));
        context.Progress.Report(0.4);

        var menuDrafts = await database.MenuDrafts.AsNoTracking()
            .ToDictionaryAsync(d => d.MenuId, cancellationToken)
            .ConfigureAwait(false);
        context.Count(ArchiveKinds.Menu, await context.Archive.WriteLinesAsync(PartName, "menus",
            Stream(database.Menus.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id), m =>
            {
                var draft = menuDrafts.GetValueOrDefault(m.Id);
                authors.Add(m.CreatedBy);
                authors.Add(m.UpdatedBy);
                authors.Add(draft?.UpdatedBy);
                return new MenuRow(
                    m.Id, m.Name, m.CreatedAtUtc, m.CreatedBy, m.UpdatedAtUtc, m.UpdatedBy,
                    draft is null ? null : new DraftRow(m.Id, draft.SchemaVersion, Parse(draft.Document), draft.UpdatedAtUtc, draft.UpdatedBy));
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        await context.Archive.WriteLinesAsync(PartName, "publications",
            Stream(database.Publications.OrderBy(p => p.SubjectKind).ThenBy(p => p.SubjectId), p =>
            {
                authors.Add(p.LiveBy);
                return new PublicationRow(p.SubjectKind, p.SubjectId, p.LiveNumber, p.NextNumber, p.LiveSinceUtc, p.LiveBy);
            }, cancellationToken), cancellationToken).ConfigureAwait(false);
        context.Progress.Report(0.5);

        context.Count("versions", await context.Archive.WriteLinesAsync(PartName, "versions",
            Stream(database.PublishedVersions.OrderBy(v => v.SubjectKind).ThenBy(v => v.SubjectId).ThenBy(v => v.Number), v =>
            {
                authors.Add(v.PublishedBy);
                return new VersionRow(
                    v.Id, v.SubjectKind, v.SubjectId, v.Number, v.SchemaVersion, Parse(v.Document), v.Title, v.Kind,
                    v.RestoredFromNumber, v.PublishedBy, v.PublishedAtUtc);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));
        context.Progress.Report(0.9);

        // Pending schedules only (FR-002). The draft version a schedule captured is this
        // installation's row version and does not travel; the captured document does.
        context.Count("schedules", await context.Archive.WriteLinesAsync(PartName, "schedules",
            Stream(database.PublishSchedules.Where(s => s.State == ScheduleStates.Pending).OrderBy(s => s.DueAtUtc).ThenBy(s => s.Id), s =>
            {
                authors.Add(s.CreatedBy);
                return new ScheduleRow(
                    s.Id, s.SubjectKind, s.SubjectId, s.Action, s.DueAtUtc, s.Revision,
                    s.CapturedDocument is null ? null : Parse(s.CapturedDocument), s.CapturedSchemaVersion, s.CapturedTitle,
                    s.RedirectToPageId, s.CreatedBy, s.CreatedAtUtc);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        context.Count(ArchiveKinds.Redirect, await context.Archive.WriteLinesAsync(PartName, "redirects",
            Stream(database.Redirects.OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id), r =>
            {
                authors.Add(r.CreatedBy);
                authors.Add(r.UpdatedBy);
                return new RedirectRow(
                    r.Id, r.MatchKind, r.SourcePath, r.SourceQuery, r.StatusCode, r.TargetKind, r.TargetPageId, r.TargetCollectionId,
                    r.TargetItemId, r.TargetPath, r.TargetUrl, r.Enabled, r.Note, r.Origin, r.CreatedAtUtc, r.CreatedBy, r.UpdatedAtUtc, r.UpdatedBy);
            }, cancellationToken), cancellationToken).ConfigureAwait(false));

        context.Progress.Report(1);
    }

    private static async IAsyncEnumerable<TRow> Stream<TEntity, TRow>(
        IQueryable<TEntity> query,
        Func<TEntity, TRow> map,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TEntity : class
    {
        await foreach (var entity in query.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return map(entity);
        }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

/// <summary><c>parts/pages/tree.jsonl</c>: one page and its place in the tree. Addresses and depth are derived.</summary>
internal sealed record PageRow(
    Guid Id,
    Guid? ParentId,
    int Position,
    string Title,
    string Slug,
    bool IsHome,
    Guid? TemplateCollectionId,
    Guid? CopiedFromId,
    DateTimeOffset? CopiedFromSavedAtUtc,
    Guid? CopiedBy,
    DateTimeOffset? CopiedAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy);

/// <summary>A draft document of a page, component or menu, as stored.</summary>
internal sealed record DraftRow(Guid SubjectId, int SchemaVersion, JsonElement Document, DateTimeOffset UpdatedAtUtc, Guid UpdatedBy);

/// <summary><c>parts/pages/components.jsonl</c>: one shared component with its draft.</summary>
internal sealed record ComponentRow(
    Guid Id,
    string Name,
    Guid? CopiedFromId,
    DateTimeOffset? CopiedFromSavedAtUtc,
    Guid? CopiedBy,
    DateTimeOffset? CopiedAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy,
    DraftRow? Draft);

/// <summary><c>parts/pages/menus.jsonl</c>: one menu with its draft (024 FR-036).</summary>
internal sealed record MenuRow(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy,
    DraftRow? Draft);

/// <summary><c>parts/pages/publications.jsonl</c>: which version of a page, component or menu is live.</summary>
internal sealed record PublicationRow(string SubjectKind, Guid SubjectId, int? LiveNumber, int NextNumber, DateTimeOffset? LiveSinceUtc, Guid? LiveBy);

/// <summary><c>parts/pages/versions.jsonl</c>: one retained published version.</summary>
internal sealed record VersionRow(
    Guid Id,
    string SubjectKind,
    Guid SubjectId,
    int Number,
    int SchemaVersion,
    JsonElement Document,
    string? Title,
    string Kind,
    int? RestoredFromNumber,
    Guid PublishedBy,
    DateTimeOffset PublishedAtUtc);

/// <summary><c>parts/pages/schedules.jsonl</c>: one pending publish or unpublish.</summary>
internal sealed record ScheduleRow(
    Guid Id,
    string SubjectKind,
    Guid SubjectId,
    string Action,
    DateTimeOffset DueAtUtc,
    int Revision,
    JsonElement? CapturedDocument,
    int? CapturedSchemaVersion,
    string? CapturedTitle,
    Guid? RedirectToPageId,
    Guid CreatedBy,
    DateTimeOffset CreatedAtUtc);

/// <summary><c>parts/pages/redirects.jsonl</c>: one redirect (017 FR-040).</summary>
internal sealed record RedirectRow(
    Guid Id,
    string MatchKind,
    string SourcePath,
    string SourceQuery,
    int StatusCode,
    string TargetKind,
    Guid? TargetPageId,
    Guid? TargetCollectionId,
    Guid? TargetItemId,
    string? TargetPath,
    string? TargetUrl,
    bool Enabled,
    string? Note,
    string Origin,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid? UpdatedBy);
