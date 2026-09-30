using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.DeleteFile;

internal sealed record DeleteFileCommand(Guid SiteId, Guid FileId, Guid UserId) : ICommand<Result>;

/// <summary>
/// Permanently deletes a file (003 US3, FR-004, clarifications Q2 and Q4, research M-13).
/// </summary>
/// <remarks>
/// <para>
/// <b>Who.</b> The endpoint declares the weaker <c>media.own_files.delete</c>, so anyone
/// with neither capability is refused before this runs. Here, deleting someone else's file
/// additionally needs <c>media.files.delete</c> — Designers and Administrators — because
/// that part of the rule depends on the row (M-18).
/// </para>
/// <para>
/// <b>Order.</b> The row goes first, and with it the file leaves the library and its address
/// on every instance, before this reports success. The bytes are removed straight after. If
/// that fails, the delete still succeeded: the orphan sweep removes the bytes later (FR-012),
/// so nothing needs a tombstone or a queue.
/// </para>
/// </remarks>
internal sealed class DeleteFileHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    IAccessEvaluator access,
    IFileStorage storage,
    ILogger<DeleteFileHandler> logger) : ICommandHandler<DeleteFileCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin delete of a file on a site named explicitly in the route (001 FR-011b).")
            .FirstOrDefaultAsync(f => f.Id == command.FileId && f.SiteId == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return Result.Failure(MediaErrors.NotFound);
        }

        if (file.UploadedBy != command.UserId
            && !await access.HasCapabilityAsync(MediaCapabilities.Delete, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(MediaErrors.Forbidden);
        }

        database.MediaFiles.Remove(file);

        // 012: pages that showed it refresh; saved with the delete, so both happen or neither.
        database.OutboxMessages.Add(OutboxMessage.From(
            new FileDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, file.SiteId, file.Id, command.UserId)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Media file {MediaFileId} deleted from site {SiteId}.", file.Id, file.SiteId);

        try
        {
            await storage.DeleteAsync(new StorageKey(file.StorageKey), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "The bytes of deleted media file {MediaFileId} on site {SiteId} could not be removed; the orphan sweep will remove them.",
                file.Id, file.SiteId);
        }

        return Result.Success();
    }
}

/// <summary>Administrative surface: permanently delete a file from a site's library.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}")]
public sealed class DeleteFileController(IDispatcher dispatcher, IAccessEvaluator access) : ControllerBase
{
    /// <summary>Deletes the file. Permanent: there is no restore (clarification Q2).</summary>
    [HttpDelete]
    [RequireCapability(MediaCapabilities.DeleteOwn)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        var command = new DeleteFileCommand(
            siteId,
            fileId,
            access.UserId ?? throw new InvalidOperationException("Delete reached without a signed-in user."));

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
