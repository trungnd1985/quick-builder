using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts.Redirects;
using QuickBuilder.Pages.Domain.Redirects;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The site's enabled redirects for the public path (017 research SE-10), on the read-only
/// connection (012 PR-06).
/// </summary>
/// <remarks>
/// Nothing is cached: one indexed lookup per request that no content answers, so a change is
/// visible on every instance at its next request (FR-021). The lookups ride the unique index
/// <c>(site_id, match_kind, source_path, source_query)</c>.
/// </remarks>
internal sealed class RedirectReader(PublishedReadDbContext database) : IRedirectReader
{
    public async Task<RedirectMatch?> FindQueryAsync(Guid siteId, string path, string canonicalQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrEmpty(canonicalQuery);

        var row = await database.Redirects
            .AsNoTracking()
            .Where(r => r.SiteId == siteId && r.Enabled && r.MatchKind == RedirectMatchKinds.Exact && r.SourcePath == path && r.SourceQuery == canonicalQuery)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Match(row, string.Empty);
    }

    public async Task<RedirectMatch?> FindAsync(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        // The exact source and every ancestor that a prefix could name, in one query: the
        // exact one first, then the longest prefix (FR-016, FR-033).
        var ancestors = Ancestors(path);
        var rows = await database.Redirects
            .AsNoTracking()
            .Where(r => r.SiteId == siteId && r.Enabled && r.SourceQuery == string.Empty
                && ((r.MatchKind == RedirectMatchKinds.Exact && r.SourcePath == path)
                    || (r.MatchKind == RedirectMatchKinds.Prefix && ancestors.Contains(r.SourcePath))))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Find(r => r.MatchKind == RedirectMatchKinds.Exact) is { } exact)
        {
            return Match(exact, string.Empty);
        }

        var prefix = rows.MaxBy(r => r.SourcePath.Length);
        return prefix is null ? null : Match(prefix, path[prefix.SourcePath.Length..]);
    }

    /// <summary>The paths above <paramref name="path"/>: <c>/a/b/c</c> gives <c>/a/b</c> and <c>/a</c>.</summary>
    internal static List<string> Ancestors(string path)
    {
        var ancestors = new List<string>();
        for (var end = path.LastIndexOf('/'); end > 0; end = path.LastIndexOf('/', end - 1))
        {
            ancestors.Add(path[..end]);
        }

        return ancestors;
    }

    private static RedirectMatch Match(RedirectRow row, string remainder) => new(
        row.Id,
        row.StatusCode,
        FromQuerySource: row.SourceQuery.Length > 0,
        row.TargetKind switch
        {
            RedirectTargetKinds.Page => RedirectTargetKind.Page,
            RedirectTargetKinds.Item => RedirectTargetKind.Item,
            RedirectTargetKinds.Path => RedirectTargetKind.Path,
            _ => RedirectTargetKind.Url,
        },
        row.TargetPageId,
        row.TargetCollectionId,
        row.TargetItemId,
        row.TargetPath,
        row.TargetUrl,
        remainder);
}
