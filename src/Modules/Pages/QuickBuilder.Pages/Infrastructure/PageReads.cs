using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The read side of the page tree: one projection per shape, shared by every slice that
/// returns it (research P-16, P-20).
/// </summary>
internal sealed class PageReads(PagesDbContext database, PagesAccess access, PublicationReads publications)
{
    /// <summary>
    /// The whole tree in one query, assembled depth-first in memory: parents before
    /// children, siblings by position.
    /// </summary>
    public async Task<PageTreeResponse> TreeAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var rows = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Select(p => new Row(p.Id, p.ParentId, p.Position, p.Depth, p.Title, p.Slug, p.Path, p.IsHome, p.Version, p.TemplateCollectionId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var children = rows.ToLookup(r => r.ParentId);
        var ordered = new List<PageNodeResponse>(rows.Count);

        // 012 FR-007: every page's publication state, in a handful of queries for the whole tree.
        var publication = await publications
            .SummariesAsync(siteId, SubjectKinds.Page, [.. rows.Select(r => r.Id)], cancellationToken)
            .ConfigureAwait(false);

        void Walk(Guid? parentId)
        {
            foreach (var row in children[parentId].OrderBy(r => r.Position))
            {
                ordered.Add(row.ToNode(children[row.Id].Count(), publication[row.Id]));
                Walk(row.Id);
            }
        }

        Walk(parentId: null);

        return new PageTreeResponse(ordered, await access.PermissionsAsync(siteId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>One page with its draft's metadata, or null when the site has no such page.</summary>
    public async Task<PageDetailResponse?> PageAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var page = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Where(p => p.Id == pageId)
            .Select(p => new
            {
                Row = new Row(p.Id, p.ParentId, p.Position, p.Depth, p.Title, p.Slug, p.Path, p.IsHome, p.Version, p.TemplateCollectionId),
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return null;
        }

        var childCount = await database.ForSite<Page>(siteId)
            .CountAsync(p => p.ParentId == pageId, cancellationToken)
            .ConfigureAwait(false);

        var draft = await DraftSummaryAsync(database.ForSite<PageDraft>(siteId).Where(d => d.PageId == pageId), cancellationToken)
            .ConfigureAwait(false);

        var node = page.Row.ToNode(
            childCount,
            await publications.SummaryAsync(siteId, SubjectKinds.Page, pageId, cancellationToken).ConfigureAwait(false));
        return new PageDetailResponse(
            node.Id,
            node.ParentId,
            node.Position,
            node.Depth,
            node.Title,
            node.Slug,
            node.Path,
            node.IsHome,
            node.ChildCount,
            node.Version,
            draft,
            page.CreatedAtUtc,
            page.UpdatedAtUtc,
            await access.PermissionsAsync(siteId, cancellationToken).ConfigureAwait(false),
            node.Publication,
            node.TemplateCollectionId);
    }

    /// <summary>The summary of the one draft <paramref name="query"/> selects.</summary>
    public async Task<DraftSummaryResponse> DraftSummaryAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        where T : Draft
    {
        var draft = await query
            .AsNoTracking()
            .Select(d => new { d.SchemaVersion, d.Version, d.UpdatedAtUtc, d.UpdatedBy })
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await access.NamesAsync([draft.UpdatedBy], cancellationToken).ConfigureAwait(false);
        return new DraftSummaryResponse(draft.SchemaVersion, draft.Version, draft.UpdatedAtUtc, PagesAccess.Reference(draft.UpdatedBy, names));
    }

    /// <summary>A draft with its document, parsed so the response carries JSON, not a string.</summary>
    public async Task<DraftResponse> DraftAsync(Draft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var names = await access.NamesAsync([draft.UpdatedBy], cancellationToken).ConfigureAwait(false);
        using var document = System.Text.Json.JsonDocument.Parse(draft.Document);
        return new DraftResponse(
            draft.SchemaVersion,
            draft.Version,
            draft.UpdatedAtUtc,
            PagesAccess.Reference(draft.UpdatedBy, names),
            document.RootElement.Clone());
    }

    private sealed record Row(
        Guid Id,
        Guid? ParentId,
        int Position,
        int Depth,
        string Title,
        string Slug,
        string Path,
        bool IsHome,
        int Version,
        Guid? TemplateCollectionId)
    {
        public PageNodeResponse ToNode(int childCount, PublicationResponse publication) =>
            new(Id, ParentId, Position, Depth, Title, Slug, Path, IsHome, childCount, Version, publication, TemplateCollectionId);
    }
}
