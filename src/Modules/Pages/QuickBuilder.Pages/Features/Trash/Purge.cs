using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Trash;

/// <summary>One entry to delete permanently: <c>page</c> or <c>component</c>, and its id.</summary>
public sealed record PurgeEntryRequest(string Kind, Guid Id);

/// <summary>Several entries to delete permanently, at most 100 (FR-021).</summary>
public sealed record PurgeManyRequest(IReadOnlyList<PurgeEntryRequest> Entries);

/// <summary>
/// Empty the trash: only entries trashed at or before <c>TrashedUpTo</c>, the moment the
/// Administrator saw the counts, and only while those counts still hold (FR-021, TR-14).
/// </summary>
public sealed record EmptyTrashRequest(DateTimeOffset TrashedUpTo, int ExpectedPages, int ExpectedComponents);

/// <summary>What emptying removed.</summary>
public sealed record EmptyTrashResponse(int Pages, int Components, IReadOnlyList<BulkFailureResponse> Failed);

internal sealed record PurgeEntryCommand(Guid SiteId, string Kind, Guid Id) : ICommand<Result>;

internal sealed record PurgeManyCommand(Guid SiteId, IReadOnlyList<PurgeEntryRequest> Entries) : ICommand<Result<BulkOutcomeResponse>>;

internal sealed record EmptyTrashCommand(Guid SiteId, EmptyTrashRequest Request) : ICommand<Result<EmptyTrashResponse>>;

/// <summary>
/// Permanent delete of trashed pages and components, one, several, or the whole trash
/// (021 US5, FR-021, FR-022, research TR-14). Administrators only.
/// </summary>
internal sealed class PurgeHandler(PagesTrash trash, PagesTrashPurge purge, PagesAccess access)
    : ICommandHandler<PurgeEntryCommand, Result>,
      ICommandHandler<PurgeManyCommand, Result<BulkOutcomeResponse>>,
      ICommandHandler<EmptyTrashCommand, Result<EmptyTrashResponse>>
{
    internal const int MaxEntries = 100;

    public async Task<Result> HandleAsync(PurgeEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return !await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false)
            ? Result.Failure(PagesErrors.SiteNotFound)
            : await purge.PurgeAsync(command.SiteId, command.Kind, command.Id, access.Actor, dueBefore: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BulkOutcomeResponse>> HandleAsync(PurgeManyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<BulkOutcomeResponse>(PagesErrors.SiteNotFound);
        }

        if (command.Entries.Count is 0 or > MaxEntries || command.Entries.Any(e => e.Kind is not (TrashKinds.Page or TrashKinds.Component)))
        {
            return Result.Failure<BulkOutcomeResponse>(PagesErrors.InvalidPlacement);
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureResponse>();
        foreach (var entry in command.Entries)
        {
            var result = await purge.PurgeAsync(command.SiteId, entry.Kind, entry.Id, access.Actor, dueBefore: null, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                succeeded.Add(entry.Id);
            }
            else
            {
                failed.Add(new BulkFailureResponse(entry.Id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new BulkOutcomeResponse(succeeded, failed));
    }

    public async Task<Result<EmptyTrashResponse>> HandleAsync(EmptyTrashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<EmptyTrashResponse>(PagesErrors.SiteNotFound);
        }

        var pages = await trash.TrashedPages(command.SiteId)
            .Where(p => p.TrashedAtUtc <= request.TrashedUpTo)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var components = await trash.TrashedComponents(command.SiteId)
            .Where(c => c.TrashedAtUtc <= request.TrashedUpTo)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // The count the Administrator typed no longer describes the trash: confirm again.
        if (pages.Count != request.ExpectedPages || components.Count != request.ExpectedComponents)
        {
            return Result.Failure<EmptyTrashResponse>(PagesErrors.CountChanged(pages.Count + components.Count));
        }

        var failed = new List<BulkFailureResponse>();
        var (purgedPages, purgedComponents) = (0, 0);
        foreach (var (kind, id) in pages.Select(id => (TrashKinds.Page, id)).Concat(components.Select(id => (TrashKinds.Component, id))))
        {
            var result = await purge.PurgeAsync(command.SiteId, kind, id, access.Actor, request.TrashedUpTo, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                failed.Add(new BulkFailureResponse(id, result.Error.Code, result.Error.Message));
            }
            else if (kind == TrashKinds.Page)
            {
                purgedPages++;
            }
            else
            {
                purgedComponents++;
            }
        }

        return Result.Success(new EmptyTrashResponse(purgedPages, purgedComponents, failed));
    }
}

/// <summary>Administrative surface: delete trashed pages and components permanently.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/trash")]
public sealed class PurgeController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Deletes one trashed page permanently.</summary>
    [HttpDelete("pages/{pageId:guid}")]
    [RequireCapability(PagesCapabilities.TrashPurge)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> PurgePageAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken) =>
        PurgeAsync(new PurgeEntryCommand(siteId, TrashKinds.Page, pageId), cancellationToken);

    /// <summary>Deletes one trashed component permanently.</summary>
    [HttpDelete("components/{componentId:guid}")]
    [RequireCapability(PagesCapabilities.TrashPurge)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> PurgeComponentAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken) =>
        PurgeAsync(new PurgeEntryCommand(siteId, TrashKinds.Component, componentId), cancellationToken);

    /// <summary>Deletes several trashed entries permanently; each succeeds or fails on its own.</summary>
    [HttpPost("purge")]
    [RequireCapability(PagesCapabilities.TrashPurge)]
    [ProducesResponseType<BulkOutcomeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurgeManyAsync(Guid siteId, [FromBody] PurgeManyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new PurgeManyCommand(siteId, request.Entries ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Empties the trash of pages and components up to the moment the counts were shown.</summary>
    [HttpPost("empty")]
    [RequireCapability(PagesCapabilities.TrashPurge)]
    [ProducesResponseType<EmptyTrashResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmptyAsync(Guid siteId, [FromBody] EmptyTrashRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new EmptyTrashCommand(siteId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    private async Task<IActionResult> PurgeAsync(PurgeEntryCommand command, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
