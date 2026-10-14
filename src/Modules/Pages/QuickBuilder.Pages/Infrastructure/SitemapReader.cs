using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// What the sitemap lists from Pages, on the read-only connection (017 research SE-14, SE-15).
/// </summary>
/// <remarks>
/// One query over the live pages and templates of a site, at most 2,000. Whether a page may be
/// listed is read from its live document with JSON containment, in the database; only a
/// template's document is brought back, to find the switch field its hiding is bound to.
/// </remarks>
internal sealed class SitemapReader(PublishedReadDbContext database) : ISitemapReader
{
    private const string NoIndex = """{"root":{"props":{"seo":{"noindex":true}}}}""";
    private const string NoSitemap = """{"root":{"props":{"seo":{"noSitemap":true}}}}""";
    private const string Canonical = """{"root":{"props":{"seo":{"canonical":{}}}}}""";

    public async Task<long> GetGenerationAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        await database.SitemapStates
            .AsNoTracking()
            .Where(s => s.SiteId == siteId)
            .Select(s => (long?)s.Generation)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

    public async Task<SitemapPages> GetPagesAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from page in database.Pages.AsNoTracking()
                where page.SiteId == siteId
                join publication in database.Publications.AsNoTracking()
                    on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
                where publication.LiveVersionId != null
                join version in database.Versions.AsNoTracking() on publication.LiveVersionId equals version.Id
                orderby page.Path
                select new
                {
                    page.Id,
                    page.Path,
                    page.TemplateCollectionId,
                    publication.LiveSinceUtc,
                    Hidden = EF.Functions.JsonContains(version.Document, NoIndex),
                    LeftOut = EF.Functions.JsonContains(version.Document, NoSitemap),
                    HasCanonical = EF.Functions.JsonContains(version.Document, Canonical),
                    TemplateDocument = page.TemplateCollectionId != null ? version.Document : null,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pages = new List<SitemapPage>();
        var templates = new List<SitemapTemplate>();
        foreach (var row in rows)
        {
            var indexable = !row.Hidden && !row.LeftOut && !row.HasCanonical;
            var liveSince = row.LiveSinceUtc ?? DateTimeOffset.UnixEpoch;
            if (row.TemplateCollectionId is { } collectionId)
            {
                templates.Add(new SitemapTemplate(row.Id, row.Path, collectionId, liveSince, indexable, HideField(row.TemplateDocument)));
            }
            else
            {
                pages.Add(new SitemapPage(row.Id, row.Path, liveSince, indexable));
            }
        }

        return new SitemapPages(pages, templates);
    }

    /// <summary>The field <c>seo.bind.noindex</c> names, if any (017 FR-007a).</summary>
    internal static string? HideField(string? document)
    {
        if (document is null)
        {
            return null;
        }

        using var parsed = JsonDocument.Parse(document);
        return parsed.RootElement.TryGetProperty("root", out var root)
            && root.TryGetProperty("props", out var props)
            && props.TryGetProperty("seo", out var seo)
            && seo.TryGetProperty("bind", out var bind)
            && bind.TryGetProperty("noindex", out var binding)
            && binding.TryGetProperty("value", out var value)
            && value.TryGetProperty("field", out var field)
            && !value.TryGetProperty("via", out _)
            && field.ValueKind == JsonValueKind.String
                ? field.GetString()
                : null;
    }
}
