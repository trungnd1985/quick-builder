using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.ListFiles;

/// <summary>What to list: one folder, or a search (013 contracts/media-api.yaml <c>listMediaFiles</c>).</summary>
internal sealed record ListFilesQuery(
    Guid SiteId,
    string? Cursor,
    int? Limit,
    string PathBase,
    Guid? FolderId = null,
    string? Scope = null,
    string? Search = null,
    string? Kind = null,
    string? Sort = null,
    string? Direction = null) : IQuery<Result<MediaFilePageDto>>;

/// <summary>
/// One page of a site's library: a folder, or a search across the library or a subtree
/// (003 FR-003, research M-16; 013 FR-005, FR-006, research MV-03).
/// </summary>
/// <remarks>
/// <para>
/// <b>Without a search</b>, the files directly in one folder, or at the top level. <b>With
/// one</b>, every file whose name or alternative text contains the term, ignoring case and
/// accents, across the whole library, or under one folder with <c>scope=subtree</c>.
/// </para>
/// <para>
/// Keyset paging per sort, with the id as tie-breaker: a file uploaded while someone pages
/// lands before their cursor or after the last page, so it never shifts a page still to come,
/// and every file appears exactly once (003 US3 scenario 1). The last page of 10,000 costs
/// what the first does (SC-001).
/// </para>
/// </remarks>
internal sealed class ListFilesHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    MediaFileMapper mapper) : IQueryHandler<ListFilesQuery, Result<MediaFilePageDto>>
{
    internal const int DefaultLimit = 50;
    internal const int MaxLimit = 100;

    public async Task<Result<MediaFilePageDto>> HandleAsync(ListFilesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            return Result.Failure<MediaFilePageDto>(MediaErrors.InvalidLimit(MaxLimit));
        }

        if (Parse(query) is not { } shape)
        {
            return Result.Failure<MediaFilePageDto>(MediaErrors.InvalidQuery(
                "Unknown sort, direction, kind or scope, a search longer than 100 characters, or scope=subtree without a folder and a search."));
        }

        ListingCursor? after = null;
        if (query.Cursor is not null)
        {
            after = ListingCursor.TryDecode(query.Cursor, shape.Sort, shape.Descending, shape.Fingerprint);
            if (after is null)
            {
                return Result.Failure<MediaFilePageDto>(MediaErrors.InvalidCursor);
            }
        }

        // Administrators pass the visibility check for any id, so a made-up site must be
        // caught here to answer like one (002 authorization.md §3, analysis I1).
        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFilePageDto>(MediaErrors.SiteNotFound);
        }

        var library = database.MediaFiles
            .AcrossAllSites("Admin listing of a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .Where(f => f.SiteId == query.SiteId);

        var filtered = await FilterAsync(library, query, shape, cancellationToken).ConfigureAwait(false);
        if (filtered.IsFailure)
        {
            return Result.Failure<MediaFilePageDto>(filtered.Error);
        }

        var matching = filtered.Value;
        var total = await matching.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await Order(After(matching, shape, after), shape)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var items = await mapper.MapAsync(rows, query.SiteId, query.PathBase, checkAvailability: true, cancellationToken)
            .ConfigureAwait(false);

        var next = hasMore ? CursorAfter(rows[^1], shape).Encode() : null;
        return Result.Success(new MediaFilePageDto(items, total, next));
    }

    /// <summary>A validated query: its filters, sort, direction and fingerprint.</summary>
    private sealed record Shape(string? Search, bool Subtree, string? Kind, ListingSort Sort, bool Descending, uint Fingerprint);

    private static Shape? Parse(ListFilesQuery query)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search is { Length: > SearchText.MaxTermLength })
        {
            return null;
        }

        var subtree = query.Scope switch
        {
            null or "" or "folder" => false,
            "subtree" => true,
            _ => (bool?)null,
        };

        ListingSort? sort = query.Sort switch
        {
            null or "" or "uploaded" => ListingSort.Uploaded,
            "name" => ListingSort.Name,
            "size" => ListingSort.Size,
            _ => null,
        };

        if (subtree is null || sort is null || query.Kind is not (null or "" or "image" or "document"))
        {
            return null;
        }

        // A subtree search needs both the folder and the term (contracts/media-api.yaml).
        if (subtree == true && (query.FolderId is null || search is null))
        {
            return null;
        }

        bool? descending = query.Direction switch
        {
            null or "" => sort != ListingSort.Name,
            "desc" => true,
            "asc" => false,
            _ => null,
        };

        if (descending is null)
        {
            return null;
        }

        var kind = string.IsNullOrEmpty(query.Kind) ? null : query.Kind;
        var fingerprint = ListingCursor.Fingerprint(
            query.FolderId?.ToString("N"), subtree.Value ? "subtree" : "folder", search is null ? null : SearchText.Normalise(search), kind);

        return new Shape(search, subtree.Value, kind, sort.Value, descending.Value, fingerprint);
    }

    private async Task<Result<IQueryable<MediaFile>>> FilterAsync(
        IQueryable<MediaFile> library, ListFilesQuery query, Shape shape, CancellationToken cancellationToken)
    {
        var folders = database.Folders
            .AcrossAllSites("Admin listing of a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .Where(f => f.SiteId == query.SiteId);

        if (shape.Subtree)
        {
            var structure = new FolderStructure(await folders.ToListAsync(cancellationToken).ConfigureAwait(false));
            if (structure.Find(query.FolderId!.Value) is null)
            {
                return Result.Failure<IQueryable<MediaFile>>(MediaErrors.FolderNotFound);
            }

            var inside = structure.Descendants(query.FolderId.Value).ToList();
            library = library.Where(f => f.FolderId != null && inside.Contains(f.FolderId.Value));
        }
        else if (query.FolderId is { } folderId)
        {
            if (!await folders.AnyAsync(f => f.Id == folderId, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<IQueryable<MediaFile>>(MediaErrors.FolderNotFound);
            }

            // Browsing a folder, or searching "everything" while one is open: a search without
            // scope=subtree covers the whole library (FR-006).
            if (shape.Search is null)
            {
                library = library.Where(f => f.FolderId == folderId);
            }
        }
        else if (shape.Search is null)
        {
            library = library.Where(f => f.FolderId == null);
        }

        if (shape.Search is { } term)
        {
            var pattern = SearchText.LikePattern(term);
            library = library.Where(f => EF.Functions.Like(f.SearchText, pattern, "\\"));
        }

        library = shape.Kind switch
        {
            "image" => library.Where(f => f.ContentType.StartsWith("image/")),
            "document" => library.Where(f => !f.ContentType.StartsWith("image/")),
            _ => library,
        };

        return Result.Success(library);
    }

    private static IQueryable<MediaFile> After(IQueryable<MediaFile> files, Shape shape, ListingCursor? cursor)
    {
        if (cursor is null)
        {
            return files;
        }

        var id = cursor.Id;
        return (shape.Sort, shape.Descending) switch
        {
            (ListingSort.Uploaded, true) => files.Where(f => EF.Functions.LessThan(
                ValueTuple.Create(f.UploadedAtUtc, f.Id), ValueTuple.Create(new DateTimeOffset(cursor.NumberKey, TimeSpan.Zero), id))),
            (ListingSort.Uploaded, false) => files.Where(f => EF.Functions.GreaterThan(
                ValueTuple.Create(f.UploadedAtUtc, f.Id), ValueTuple.Create(new DateTimeOffset(cursor.NumberKey, TimeSpan.Zero), id))),
            (ListingSort.Size, true) => files.Where(f => EF.Functions.LessThan(
                ValueTuple.Create(f.SizeBytes, f.Id), ValueTuple.Create(cursor.NumberKey, id))),
            (ListingSort.Size, false) => files.Where(f => EF.Functions.GreaterThan(
                ValueTuple.Create(f.SizeBytes, f.Id), ValueTuple.Create(cursor.NumberKey, id))),
            (ListingSort.Name, true) => files.Where(f => EF.Functions.LessThan(
                ValueTuple.Create(f.SortName, f.Id), ValueTuple.Create(cursor.TextKey ?? string.Empty, id))),
            _ => files.Where(f => EF.Functions.GreaterThan(
                ValueTuple.Create(f.SortName, f.Id), ValueTuple.Create(cursor.TextKey ?? string.Empty, id))),
        };
    }

    private static IQueryable<MediaFile> Order(IQueryable<MediaFile> files, Shape shape) =>
        (shape.Sort, shape.Descending) switch
        {
            (ListingSort.Uploaded, true) => files.OrderByDescending(f => f.UploadedAtUtc).ThenByDescending(f => f.Id),
            (ListingSort.Uploaded, false) => files.OrderBy(f => f.UploadedAtUtc).ThenBy(f => f.Id),
            (ListingSort.Size, true) => files.OrderByDescending(f => f.SizeBytes).ThenByDescending(f => f.Id),
            (ListingSort.Size, false) => files.OrderBy(f => f.SizeBytes).ThenBy(f => f.Id),
            (ListingSort.Name, true) => files.OrderByDescending(f => f.SortName).ThenByDescending(f => f.Id),
            _ => files.OrderBy(f => f.SortName).ThenBy(f => f.Id),
        };

    private static ListingCursor CursorAfter(MediaFile last, Shape shape) => shape.Sort switch
    {
        ListingSort.Uploaded => new ListingCursor(shape.Sort, shape.Descending, shape.Fingerprint, last.Id, last.UploadedAtUtc.UtcTicks, null),
        ListingSort.Size => new ListingCursor(shape.Sort, shape.Descending, shape.Fingerprint, last.Id, last.SizeBytes, null),
        _ => new ListingCursor(shape.Sort, shape.Descending, shape.Fingerprint, last.Id, 0, last.SortName),
    };
}

/// <summary>Administrative surface: browse or search a site's library.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files")]
public sealed class ListFilesController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>One page of a folder, or of a search (013 contracts/media-api.yaml).</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid siteId,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        [FromQuery] Guid? folderId,
        [FromQuery] string? scope,
        [FromQuery] string? q,
        [FromQuery] string? kind,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        CancellationToken cancellationToken)
    {
        var query = new ListFilesQuery(
            siteId, cursor, limit, Request.PathBase.Value ?? string.Empty, folderId, scope, q, kind, sort, direction);

        var result = await dispatcher.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
