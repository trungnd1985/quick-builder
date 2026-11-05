using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Features.Transfer.Engine;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer;

/// <param name="Audit">True when this request starts a download; resumed ranges are not recorded again.</param>
internal sealed record DownloadArchiveQuery(Guid TransferId, bool Audit) : IQuery<Result<ArchiveDownload>>;

/// <summary>An archive opened for download. The caller disposes it.</summary>
internal sealed record ArchiveDownload(StoredFile File, string FileName) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => File.DisposeAsync();
}

/// <summary>
/// Opens a finished export for download (020 FR-008). The stored object is always seekable, so
/// the response honours ranges and an interrupted download can resume.
/// </summary>
internal sealed class DownloadArchiveHandler(
    TransferJobStore store,
    IFileStorage storage,
    ISiteDirectory sites,
    IAccessEvaluator access,
    TimeProvider clock)
    : IQueryHandler<DownloadArchiveQuery, Result<ArchiveDownload>>
{
    public async Task<Result<ArchiveDownload>> HandleAsync(DownloadArchiveQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var job = await store.FindAsync(query.TransferId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Kind != TransferKinds.Export)
        {
            return Result.Failure<ArchiveDownload>(TransferErrors.NotFound);
        }

        var now = clock.GetUtcNow();
        if (!job.IsDownloadable(now))
        {
            return Result.Failure<ArchiveDownload>(TransferErrors.ArchiveGone);
        }

        var file = await storage.OpenReadAsync(new StorageKey(job.ArchiveKey!), cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Result.Failure<ArchiveDownload>(TransferErrors.ArchiveGone);
        }

        if (query.Audit)
        {
            // Recording the download goes through the job row, so it is one transaction with the
            // audit event; a failure to record it does not stop the download.
            await store.SaveAsync(job, [PendingEvent.Of(new SiteArchiveDownloaded(Guid.CreateVersion7(), now, job.Id, job.SiteId!.Value, access.UserId))], cancellationToken)
                .ConfigureAwait(false);
        }

        var site = await sites.FindByIdAsync(job.SiteId!.Value, cancellationToken).ConfigureAwait(false);
        var name = $"{site?.PrimaryHostName ?? job.SiteId.Value.ToString("N")}-{job.FinishedAtUtc!.Value.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}{ArchiveFormat.FileExtension}";
        return Result.Success(new ArchiveDownload(file, name));
    }
}

internal sealed record DeleteArchiveCommand(Guid TransferId) : ICommand<Result>;

/// <summary>Deletes a transfer's stored archive now (020 FR-008). The job's row stays, with its report.</summary>
internal sealed class DeleteArchiveHandler(TransferJobStore store, IFileStorage storage, IAccessEvaluator access, TimeProvider clock)
    : ICommandHandler<DeleteArchiveCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteArchiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await store.FindAsync(command.TransferId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return Result.Failure(TransferErrors.NotFound);
        }

        if (job.ArchiveKey is null)
        {
            return Result.Success();
        }

        if (TransferStates.Leased.Contains(job.State) || job.State == TransferStates.Confirmed)
        {
            return Result.Failure(TransferErrors.Busy);
        }

        var now = clock.GetUtcNow();
        await storage.DeleteAsync(new StorageKey(job.ArchiveKey), cancellationToken).ConfigureAwait(false);
        job.ArchiveDeleted(now);
        await store.SaveAsync(job, [PendingEvent.Of(new SiteArchiveDeleted(Guid.CreateVersion7(), now, job.Id, job.SiteId, access.UserId))], cancellationToken)
            .ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Administrative surface: archives (020 contracts/transfer-api.yaml).</summary>
[ApiController]
[Route("api/v1/transfers/{transferId:guid}")]
public sealed class TransferArchiveController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Downloads a finished export. Supports <c>Range</c>, so a download can resume.</summary>
    [HttpGet("archive")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> DownloadAsync(Guid transferId, CancellationToken cancellationToken)
    {
        // A resumed download asks for a range past the start; record only the first request.
        var range = Request.Headers.Range.ToString();
        var audit = string.IsNullOrEmpty(range) || range.StartsWith("bytes=0-", StringComparison.Ordinal);

        var result = await dispatcher.QueryAsync(new DownloadArchiveQuery(transferId, audit), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ErrorResults.ToActionResult(result.Error);
        }

        // The response disposes the stream when it has been sent.
        return File(result.Value.File.Content, ArchiveFormat.MediaType, result.Value.FileName, enableRangeProcessing: true);
    }

    /// <summary>Deletes the stored archive now; the transfer and its report stay.</summary>
    [HttpDelete]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteArchiveCommand(transferId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
