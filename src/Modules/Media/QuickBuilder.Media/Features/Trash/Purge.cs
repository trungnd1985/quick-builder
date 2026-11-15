using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.Trash;

/// <summary>Several trashed files to delete permanently, at most 100 (FR-021).</summary>
public sealed record PurgeManyRequest(IReadOnlyList<Guid> Ids);

/// <summary>Empty the media trash up to the moment the count was shown (FR-021, TR-14).</summary>
public sealed record EmptyTrashRequest(DateTimeOffset TrashedUpTo, int ExpectedFiles);

/// <summary>What emptying removed.</summary>
public sealed record EmptyTrashResponse(int Files, IReadOnlyList<BulkFailureResponse> Failed);

internal sealed record PurgeFileCommand(Guid SiteId, Guid FileId) : ICommand<Result>;

internal sealed record PurgeFilesCommand(Guid SiteId, IReadOnlyList<Guid> Ids) : ICommand<Result<BulkOutcomeResponse>>;

internal sealed record EmptyFileTrashCommand(Guid SiteId, EmptyTrashRequest Request) : ICommand<Result<EmptyTrashResponse>>;

/// <summary>Permanent delete of trashed files (021 US5, FR-021, FR-022). Administrators only.</summary>
internal sealed class PurgeHandler(MediaTrash trash, MediaTrashPurge purge, ISiteDirectory sites, IAccessEvaluator access)
    : ICommandHandler<PurgeFileCommand, Result>,
      ICommandHandler<PurgeFilesCommand, Result<BulkOutcomeResponse>>,
      ICommandHandler<EmptyFileTrashCommand, Result<EmptyTrashResponse>>
{
    private const int MaxEntries = 100;

    public async Task<Result> HandleAsync(PurgeFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null
            ? Result.Failure(MediaErrors.SiteNotFound)
            : await purge.PurgeAsync(command.SiteId, command.FileId, access.UserId, dueBefore: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BulkOutcomeResponse>> HandleAsync(PurgeFilesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<BulkOutcomeResponse>(MediaErrors.SiteNotFound);
        }

        if (command.Ids.Count is 0 or > MaxEntries)
        {
            return Result.Failure<BulkOutcomeResponse>(MediaErrors.InvalidQuery("ids"));
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureResponse>();
        foreach (var id in command.Ids.Distinct())
        {
            var result = await purge.PurgeAsync(command.SiteId, id, access.UserId, dueBefore: null, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                succeeded.Add(id);
            }
            else
            {
                failed.Add(new BulkFailureResponse(id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new BulkOutcomeResponse(succeeded, failed));
    }

    public async Task<Result<EmptyTrashResponse>> HandleAsync(EmptyFileTrashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<EmptyTrashResponse>(MediaErrors.SiteNotFound);
        }

        var ids = await trash.TrashedFiles(command.SiteId)
            .Where(i => i.TrashedAtUtc <= request.TrashedUpTo)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count != request.ExpectedFiles)
        {
            return Result.Failure<EmptyTrashResponse>(MediaErrors.CountChanged);
        }

        var failed = new List<BulkFailureResponse>();
        var purged = 0;
        foreach (var id in ids)
        {
            var result = await purge.PurgeAsync(command.SiteId, id, access.UserId, request.TrashedUpTo, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                purged++;
            }
            else
            {
                failed.Add(new BulkFailureResponse(id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new EmptyTrashResponse(purged, failed));
    }
}

/// <summary>Administrative surface: delete trashed files permanently.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/trash")]
public sealed class PurgeController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Deletes one trashed file permanently.</summary>
    [HttpDelete("files/{fileId:guid}")]
    [RequireCapability(MediaCapabilities.TrashPurge)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurgeAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new PurgeFileCommand(siteId, fileId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : NoContent();
    }

    /// <summary>Deletes several trashed files permanently; each succeeds or fails on its own.</summary>
    [HttpPost("purge")]
    [RequireCapability(MediaCapabilities.TrashPurge)]
    [ProducesResponseType<BulkOutcomeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurgeManyAsync(Guid siteId, [FromBody] PurgeManyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new PurgeFilesCommand(siteId, request.Ids ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Empties the media trash up to the moment the count was shown.</summary>
    [HttpPost("empty")]
    [RequireCapability(MediaCapabilities.TrashPurge)]
    [ProducesResponseType<EmptyTrashResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmptyAsync(Guid siteId, [FromBody] EmptyTrashRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new EmptyFileTrashCommand(siteId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
