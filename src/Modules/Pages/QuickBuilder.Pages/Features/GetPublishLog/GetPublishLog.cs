using System.Buffers.Binary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.PublishingHistory;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetPublishLog;

internal sealed record GetPublishLogQuery(
    Guid SiteId,
    string? SubjectKind,
    Guid? SubjectId,
    Guid? UserId,
    string? Cursor,
    int? Limit) : IQuery<Result<PublishLogResponse>>;

/// <summary>
/// Every publishing action on a site, newest first, including pages and components that no
/// longer exist (012 FR-017, clarification Q2).
/// </summary>
/// <remarks>
/// Keyset paging on <c>(occurred_at_utc, id)</c>, descending, as the media library does: an
/// entry written while someone pages lands before their cursor, so every entry appears
/// exactly once. The cursor is the position of the last row, base64url.
/// </remarks>
internal sealed class GetPublishLogHandler(PagesDbContext database, PagesAccess access, LogEntryReads entries)
    : IQueryHandler<GetPublishLogQuery, Result<PublishLogResponse>>
{
    internal const int DefaultLimit = 50;
    internal const int MaxLimit = 100;

    public async Task<Result<PublishLogResponse>> HandleAsync(GetPublishLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            return Result.Failure<PublishLogResponse>(PagesErrors.InvalidLimit(MaxLimit));
        }

        Cursor? after = null;
        if (query.Cursor is not null)
        {
            after = Cursor.TryDecode(query.Cursor);
            if (after is null)
            {
                return Result.Failure<PublishLogResponse>(PagesErrors.InvalidCursor);
            }
        }

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PublishLogResponse>(PagesErrors.SiteNotFound);
        }

        var log = database.ForSite<PublishLogEntry>(query.SiteId).AsNoTracking();

        if (!string.IsNullOrEmpty(query.SubjectKind))
        {
            log = log.Where(e => e.SubjectKind == query.SubjectKind);
        }

        if (query.SubjectId is { } subjectId)
        {
            log = log.Where(e => e.SubjectId == subjectId);
        }

        if (query.UserId is { } userId)
        {
            log = log.Where(e => e.ActorUserId == userId || e.ScheduleOwnerId == userId);
        }

        if (after is { } position)
        {
            log = log.Where(e => EF.Functions.LessThan(
                ValueTuple.Create(e.OccurredAtUtc, e.Id),
                ValueTuple.Create(position.OccurredAtUtc, position.Id)));
        }

        var rows = await log
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var existing = await ExistingAsync(query.SiteId, rows, cancellationToken).ConfigureAwait(false);
        var items = await entries
            .MapAsync(rows, e => e.SubjectKind == SubjectKinds.Design || existing.Contains(e.SubjectId), cancellationToken)
            .ConfigureAwait(false);

        var next = hasMore ? new Cursor(rows[^1].OccurredAtUtc, rows[^1].Id).Encode() : null;
        return Result.Success(new PublishLogResponse(items, next));
    }

    /// <summary>Which of the entries' pages and components still exist.</summary>
    private async Task<HashSet<Guid>> ExistingAsync(Guid siteId, List<PublishLogEntry> rows, CancellationToken cancellationToken)
    {
        var pageIds = rows.Where(e => e.SubjectKind == SubjectKinds.Page).Select(e => e.SubjectId).Distinct().ToList();
        var componentIds = rows.Where(e => e.SubjectKind == SubjectKinds.Component).Select(e => e.SubjectId).Distinct().ToList();

        var pages = pageIds.Count == 0
            ? []
            : await database.ForSite<Page>(siteId).Where(p => pageIds.Contains(p.Id)).Select(p => p.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        var components = componentIds.Count == 0
            ? []
            : await database.ForSite<Component>(siteId).Where(c => componentIds.Contains(c.Id)).Select(c => c.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. pages, .. components];
    }

    /// <summary>A position in the log: the last row a page returned.</summary>
    internal readonly record struct Cursor(DateTimeOffset OccurredAtUtc, Guid Id)
    {
        private const int EncodedLength = 8 + 16;

        public string Encode()
        {
            Span<byte> bytes = stackalloc byte[EncodedLength];
            BinaryPrimitives.WriteInt64BigEndian(bytes, OccurredAtUtc.UtcTicks);
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

/// <summary>Administrative surface: the site's publish log (Administrators only).</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/publish-log")]
public sealed class GetPublishLogController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PublishingCapabilities.LogView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid siteId,
        [FromQuery] string? subjectKind,
        [FromQuery] Guid? subjectId,
        [FromQuery] Guid? userId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .QueryAsync(new GetPublishLogQuery(siteId, subjectKind, subjectId, userId, cursor, limit), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
