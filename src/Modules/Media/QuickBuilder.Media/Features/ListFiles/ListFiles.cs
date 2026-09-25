using System.Buffers.Binary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.ListFiles;

internal sealed record ListFilesQuery(Guid SiteId, string? Cursor, int? Limit, string PathBase)
    : IQuery<Result<MediaFilePageDto>>;

/// <summary>
/// One page of a site's library, newest first (003 FR-003, research M-16).
/// </summary>
/// <remarks>
/// Keyset paging on <c>(uploaded_at_utc, id)</c>, descending: a file uploaded while someone
/// pages lands before their cursor, so it can never shift a page still to come — every file
/// appears exactly once (US3 scenario 1), and the last page of 10,000 costs what the first
/// does (SC-009a). The cursor is opaque to clients: the position of the last row, base64url.
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

        Cursor? after = null;
        if (query.Cursor is not null)
        {
            after = Cursor.TryDecode(query.Cursor);
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

        var total = await library.CountAsync(cancellationToken).ConfigureAwait(false);

        var page = library;
        if (after is { } position)
        {
            page = page.Where(f => EF.Functions.LessThan(
                ValueTuple.Create(f.UploadedAtUtc, f.Id),
                ValueTuple.Create(position.UploadedAtUtc, position.Id)));
        }

        var rows = await page
            .OrderByDescending(f => f.UploadedAtUtc)
            .ThenByDescending(f => f.Id)
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

        var next = hasMore ? new Cursor(rows[^1].UploadedAtUtc, rows[^1].Id).Encode() : null;
        return Result.Success(new MediaFilePageDto(items, total, next));
    }

    /// <summary>A position in the listing: the last row a page returned.</summary>
    internal readonly record struct Cursor(DateTimeOffset UploadedAtUtc, Guid Id)
    {
        private const int EncodedLength = 8 + 16;

        public string Encode()
        {
            Span<byte> bytes = stackalloc byte[EncodedLength];
            BinaryPrimitives.WriteInt64BigEndian(bytes, UploadedAtUtc.UtcTicks);
            Id.TryWriteBytes(bytes[8..]);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static Cursor? TryDecode(string value)
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

            Span<byte> bytes = stackalloc byte[EncodedLength + 3];
            if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != EncodedLength)
            {
                return null;
            }

            var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
            if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return null;
            }

            return new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes[8..EncodedLength]));
        }
    }
}

/// <summary>Administrative surface: browse a site's library.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files")]
public sealed class ListFilesController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>One page of the site's library, newest first.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid siteId,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .QueryAsync(new ListFilesQuery(siteId, cursor, limit, Request.PathBase.Value ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
