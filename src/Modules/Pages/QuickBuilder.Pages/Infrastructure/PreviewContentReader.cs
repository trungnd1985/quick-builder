using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// <see cref="IPreviewContentReader"/> over the read-only preview context (022 research PV-02).
/// </summary>
/// <remarks>
/// Every document leaves at the current schema version through <see cref="Schema.StoredDocuments"/>,
/// as published ones do, so the renderer receives what publishing would hand it (spec FR-002).
/// Each method is one query. Titles, paths, names and documents are content: never logged.
/// </remarks>
internal sealed class PreviewContentReader(PreviewReadDbContext database, Schema.StoredDocuments stored) : IPreviewContentReader
{
    public async Task<PreviewPage?> GetPageDraftAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default)
    {
        var row = await DraftsOf(database.Pages.Where(p => p.SiteId == siteId && p.Id == pageId), cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToPreviewPage(row);
    }

    public async Task<PreviewPage?> GetTemplateDraftAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default)
    {
        var row = await DraftsOf(database.Pages.Where(p => p.SiteId == siteId && p.TemplateCollectionId == collectionId), cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToPreviewPage(row);
    }

    public async Task<PreviewPageVersion?> GetPageVersionAsync(Guid siteId, Guid pageId, int number, CancellationToken cancellationToken = default)
    {
        var row = await (
                from page in database.Pages
                where page.SiteId == siteId && page.Id == pageId
                join version in database.Versions
                    on new { Kind = SubjectKinds.Page, page.Id } equals new { Kind = version.SubjectKind, Id = version.SubjectId }
                where version.Number == number
                select new { page, version })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToVersion(row.page, row.version);
    }

    public async Task<PreviewPageVersion?> GetLiveTemplateVersionAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default)
    {
        var row = await (
                from page in database.Pages
                where page.SiteId == siteId && page.Id == pageId
                join publication in database.Publications
                    on new { Kind = SubjectKinds.Page, page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
                join version in database.Versions on publication.LiveVersionId equals version.Id
                select new { page, version })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToVersion(row.page, row.version);
    }

    public async Task<IReadOnlyDictionary<Guid, PreviewDraftDocument>> GetUnpublishedComponentDraftsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(componentIds);
        if (componentIds.Count == 0)
        {
            return new Dictionary<Guid, PreviewDraftDocument>();
        }

        var rows = await (
                from component in database.Components
                where component.SiteId == siteId && componentIds.Contains(component.Id)
                join draft in database.ComponentDrafts on component.Id equals draft.ComponentId
                where !database.Publications.Any(p =>
                    p.SubjectKind == SubjectKinds.Component && p.SubjectId == component.Id && p.LiveVersionId != null)
                select new { component.Id, component.Name, draft.SchemaVersion, draft.Document })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            r => r.Id,
            r => Draft(SubjectKinds.Component, r.Id, r.Document, r.SchemaVersion, r.Name));
    }

    public async Task<IReadOnlyDictionary<Guid, PreviewDraftDocument>> GetUnpublishedMenuDraftsAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(menuIds);
        if (menuIds.Count == 0)
        {
            return new Dictionary<Guid, PreviewDraftDocument>();
        }

        var rows = await (
                from menu in database.Menus
                where menu.SiteId == siteId && menuIds.Contains(menu.Id)
                join draft in database.MenuDrafts on menu.Id equals draft.MenuId
                where !database.Publications.Any(p =>
                    p.SubjectKind == SubjectKinds.Menu && p.SubjectId == menu.Id && p.LiveVersionId != null)
                select new { menu.Id, menu.Name, draft.SchemaVersion, draft.Document })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            r => r.Id,
            r => Draft(SubjectKinds.Menu, r.Id, r.Document, r.SchemaVersion, r.Name));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageIds);
        if (pageIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await database.Pages
            .Where(p => p.SiteId == siteId && pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The first of <paramref name="pages"/> with its draft, its live state and its pending
    /// scheduled publish. Filtered before the projection, so the whole read is one query.
    /// </summary>
    private async Task<DraftRow?> DraftsOf(IQueryable<PreviewPageRow> pages, CancellationToken cancellationToken)
    {
        var row = await (
                from page in pages
                join draft in database.PageDrafts on page.Id equals draft.PageId
                select new
                {
                    page,
                    draft,
                    IsLive = database.Publications.Any(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == page.Id && p.LiveVersionId != null),
                    Scheduled = database.Schedules
                        .Where(s => s.SubjectKind == SubjectKinds.Page && s.SubjectId == page.Id
                            && s.State == ScheduleStates.Pending && s.Action == ScheduleActions.Publish)
                        .OrderBy(s => s.DueAtUtc)
                        .Select(s => (DateTimeOffset?)s.DueAtUtc)
                        .FirstOrDefault(),
                })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : new DraftRow(row.page, row.draft, row.IsLive, row.Scheduled);
    }

    private PreviewPage ToPreviewPage(DraftRow row)
    {
        var (version, document) = Current(SubjectKinds.Page, row.Page.Id, row.Draft.Document, row.Draft.SchemaVersion);
        var isTemplate = row.Page.TemplateCollectionId is not null;
        return new PreviewPage(
            row.Page.Id,
            row.Page.Title,
            isTemplate ? null : row.Page.Path,
            isTemplate,
            row.Page.TemplateCollectionId,
            version,
            document,
            row.Draft.UpdatedAtUtc,
            row.Draft.UpdatedBy,
            row.PendingScheduleAtUtc,
            row.IsLive);
    }

    private PreviewPageVersion ToVersion(PreviewPageRow page, PreviewVersionRow version)
    {
        var (schemaVersion, document) = Current(SubjectKinds.Page, page.Id, version.Document, version.SchemaVersion);
        return new PreviewPageVersion(
            page.Id,
            version.Number,
            version.Title ?? page.Title,
            page.TemplateCollectionId is null ? page.Path : null,
            schemaVersion,
            document,
            version.PublishedAtUtc,
            version.PublishedBy);
    }

    private PreviewDraftDocument Draft(string kind, Guid id, string document, int schemaVersion, string name)
    {
        var (version, current) = Current(kind, id, document, schemaVersion);
        return new PreviewDraftDocument(version, current, name);
    }

    // Parsed once per read and detached from the pooled buffer, so the caller may keep it.
    private (int Version, JsonElement Document) Current(string kind, Guid id, string document, int schemaVersion)
    {
        using var parsed = JsonDocument.Parse(document);
        var (version, current) = stored.ForRendering(kind, id, parsed.RootElement, schemaVersion);
        return (version, current.Clone());
    }

    private sealed record DraftRow(PreviewPageRow Page, PreviewDraftRow Draft, bool IsLive, DateTimeOffset? PendingScheduleAtUtc);
}
