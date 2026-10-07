using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.GetSchemaHistory;

internal sealed record GetSchemaHistoryQuery(Guid SiteId, Guid CollectionId, int? Before, int Limit)
    : IQuery<Result<SchemaHistoryResponse>>;

/// <summary>One saved revision: who, when, and its change entries (data-model §3.4).</summary>
public sealed record HistoryEntryResponse(int Revision, DateTimeOffset ChangedAtUtc, PersonResponse ChangedBy, JsonNode Changes);

/// <summary>A page of a collection's history, newest first.</summary>
public sealed record SchemaHistoryResponse(IReadOnlyList<HistoryEntryResponse> Entries, int? NextBefore);

/// <summary>A collection's saved revisions, newest first (US6, FR-033, research CS-13).</summary>
internal sealed class GetSchemaHistoryHandler(CmsDbContext database, CmsAccess access)
    : IQueryHandler<GetSchemaHistoryQuery, Result<SchemaHistoryResponse>>
{
    public async Task<Result<SchemaHistoryResponse>> HandleAsync(GetSchemaHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<SchemaHistoryResponse>(CmsErrors.SiteNotFound);
        }

        if (!await database.ForSite<Collection>(query.SiteId).AnyAsync(c => c.Id == query.CollectionId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<SchemaHistoryResponse>(CmsErrors.CollectionNotFound);
        }

        var limit = Math.Clamp(query.Limit, 1, 100);
        var entries = database.ForSite<SchemaChangeEntry>(query.SiteId)
            .AsNoTracking()
            .Where(e => e.CollectionId == query.CollectionId);

        if (query.Before is { } before)
        {
            entries = entries.Where(e => e.Revision < before);
        }

        var page = await entries
            .OrderByDescending(e => e.Revision)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var shown = page.Take(limit).ToList();
        var names = await access.NamesAsync(shown.Select(e => e.ChangedBy), cancellationToken).ConfigureAwait(false);

        return Result.Success(new SchemaHistoryResponse(
            [.. shown.Select(e => new HistoryEntryResponse(
                e.Revision,
                e.ChangedAtUtc,
                new PersonResponse(e.ChangedBy, names.GetValueOrDefault(e.ChangedBy, string.Empty)),
                JsonNode.Parse(e.Summary)!))],
            page.Count > limit ? shown[^1].Revision : null));
    }
}

/// <summary>Administrative surface: a collection's schema history.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/history")]
public sealed class GetSchemaHistoryController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid siteId,
        Guid collectionId,
        [FromQuery] int? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await dispatcher.QueryAsync(new GetSchemaHistoryQuery(siteId, collectionId, before, limit), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
