using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Contracts.Media;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.DeleteFile;

internal sealed record DeleteFileCommand(Guid SiteId, Guid FileId, Guid UserId, bool ConfirmInUse = false) : ICommand<DeleteFileOutcome>;

/// <summary>How a delete ended. A <c>media.in_use</c> refusal carries the usage to show (013 FR-015).</summary>
internal sealed record DeleteFileOutcome(Error? Error, MediaUsageSummary? Usage = null)
{
    public static DeleteFileOutcome Deleted { get; } = new(Error: null);

    public static DeleteFileOutcome Refused(Error error, MediaUsageSummary? usage = null) => new(error, usage);
}

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
/// <para>
/// <b>In use (013 MV-06).</b> A file a published page shows needs the publishing capability as well
/// (Designers, Administrators), whoever uploaded it; one any page or component uses needs
/// <c>confirmInUse</c>, and is otherwise refused with the usage to show. The usage is read at
/// the moment of the delete and the row version guards the row itself. A page that starts
/// using the file between that read and the commit is the accepted race: it shows nothing
/// where the image was from its next request, exactly as after a confirmed delete.
/// </para>
/// </remarks>
internal sealed class DeleteFileHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    IAccessEvaluator access,
    IFileStorage storage,
    IMediaUsageReader usage,
    ILogger<DeleteFileHandler> logger) : ICommandHandler<DeleteFileCommand, DeleteFileOutcome>
{
    public async Task<DeleteFileOutcome> HandleAsync(DeleteFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return DeleteFileOutcome.Refused(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin delete of a file on a site named explicitly in the route (001 FR-011b).")
            .FirstOrDefaultAsync(f => f.Id == command.FileId && f.SiteId == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return DeleteFileOutcome.Refused(MediaErrors.NotFound);
        }

        if (file.UploadedBy != command.UserId
            && !await access.HasCapabilityAsync(MediaCapabilities.Delete, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return DeleteFileOutcome.Refused(MediaErrors.Forbidden);
        }

        var used = (await usage.SummariseAsync(command.SiteId, [file.Id], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(file.Id) ?? MediaUsageSummary.None;

        if (used.InLiveUse
            && !await access.HasCapabilityAsync(PublishingCapabilities.Manage, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return DeleteFileOutcome.Refused(MediaErrors.InLiveUse);
        }

        if (used.InUse && !command.ConfirmInUse)
        {
            return DeleteFileOutcome.Refused(MediaErrors.InUse, used);
        }

        database.MediaFiles.Remove(file);

        // 012: pages that showed it refresh; saved with the delete, so both happen or neither.
        database.OutboxMessages.Add(OutboxMessage.From(
            new FileDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, file.SiteId, file.Id, command.UserId)));
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Replaced, moved or already deleted since it was read.
            return DeleteFileOutcome.Refused(MediaErrors.Conflict);
        }

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

        return DeleteFileOutcome.Deleted;
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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid fileId, [FromQuery] bool confirmInUse, CancellationToken cancellationToken)
    {
        var command = new DeleteFileCommand(
            siteId,
            fileId,
            access.UserId ?? throw new InvalidOperationException("Delete reached without a signed-in user."),
            confirmInUse);

        var outcome = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (outcome.Error is null)
        {
            return NoContent();
        }

        return outcome.Usage is { } used
            ? MediaErrorResults.ToActionResult(outcome.Error, new Dictionary<string, object?> { ["usage"] = MediaFileMapper.ToDto(used) })
            : MediaErrorResults.ToActionResult(outcome.Error);
    }
}
