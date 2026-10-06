using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Features.UploadFile;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Pages.Contracts.Media;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.ReplaceFile;

internal sealed record ReplaceFileCommand(
    Guid SiteId,
    Guid FileId,
    Guid UserId,
    int ExpectedContentVersion,
    string? RawFileName,
    string? DeclaredType,
    Stream Body,
    string PathBase) : ICommand<Result<MediaFileDto>>;

/// <summary>
/// Replaces a file's contents, keeping its identity (013 US6, FR-017 to FR-020; research
/// MV-06, MV-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>A new version, a new address.</b> The new bytes are stored under the next version's own
/// key, so nothing ever overwrites a stored object, and the file's address changes with its
/// version: the old one is not found from the commit on, and no request to the new one can
/// return old bytes. Pages hold the id, so they show the new contents at their next request
/// once <c>FileChanged(replaced)</c> has refreshed them.
/// </para>
/// <para>
/// <b>Who.</b> As for delete: another person's file needs <c>media.files.delete</c>, and a file
/// published pages show needs the publishing capability as well (013 clarifications). The
/// uploader stays the original one; the replacement is recorded separately.
/// </para>
/// <para>
/// <b>All or nothing.</b> The row is changed and the event written in one save, guarded by the
/// version the member saw and the row version. Until then the file is unchanged; if the save
/// fails, the new bytes are removed. The old bytes are removed after the commit, or by the
/// orphan sweep if that fails.
/// </para>
/// </remarks>
internal sealed class ReplaceFileHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    IAccessEvaluator access,
    IMediaUsageReader usage,
    UploadPipeline pipeline,
    MediaFileMapper mapper,
    ThumbnailQueue thumbnails,
    TimeProvider clock,
    ILogger<ReplaceFileHandler> logger) : ICommandHandler<ReplaceFileCommand, Result<MediaFileDto>>
{
    public async Task<Result<MediaFileDto>> HandleAsync(ReplaceFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin replace of a file on a site named explicitly in the route (001 FR-011b).")
            .FirstOrDefaultAsync(f => f.Id == command.FileId && f.SiteId == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.NotFound);
        }

        if (file.ContentVersion != command.ExpectedContentVersion)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.Conflict);
        }

        var rights = await MediaFileRules.RightsAsync(access, command.SiteId, cancellationToken).ConfigureAwait(false);
        if (!rights.DeleteAny && file.UploadedBy != command.UserId)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.Forbidden);
        }

        var used = (await usage.SummariseAsync(command.SiteId, [file.Id], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(file.Id) ?? MediaUsageSummary.None;
        if (used.InLiveUse && !rights.Publish)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.InLiveUse);
        }

        var previousKey = new StorageKey(file.StorageKey);
        var key = MediaStorageKeys.For(command.SiteId, file.Id, file.ContentVersion + 1);

        var stored = await pipeline.StoreAsync(key, command.RawFileName, command.DeclaredType, command.Body, cancellationToken)
            .ConfigureAwait(false);
        if (stored.IsFailure)
        {
            return Result.Failure<MediaFileDto>(stored.Error);
        }

        var upload = stored.Value;
        try
        {
            var replaced = file.Replace(upload.Type, upload.Extension, upload.SizeBytes, key.Value, upload.Metadata, command.UserId, clock.GetUtcNow(), upload.MetadataState);
            if (replaced.IsFailure)
            {
                await pipeline.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
                return Result.Failure<MediaFileDto>(replaced.Error);
            }

            database.OutboxMessages.Add(OutboxMessage.From(new FileChanged(
                Guid.CreateVersion7(), clock.GetUtcNow(), file.SiteId, file.Id, FileChangeReasons.Replaced)));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Replaced, moved or deleted since it was read: the member's version is stale.
            await pipeline.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
            return Result.Failure<MediaFileDto>(MediaErrors.Conflict);
        }
        catch
        {
            await pipeline.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // Identifiers only (003 FR-030).
        logger.LogInformation(
            "Media file {MediaFileId} on site {SiteId} replaced with version {ContentVersion}.",
            file.Id, file.SiteId, file.ContentVersion);

        // The old original goes now; its variants go with the next sweep (MV-14).
        await pipeline.RemoveAsync(previousKey, CancellationToken.None).ConfigureAwait(false);
        thumbnails.Enqueue(file);

        var dto = await mapper.MapAsync([file], command.SiteId, command.PathBase, checkAvailability: false, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(dto[0]);
    }
}

/// <summary>Administrative surface: replace a file's contents with the raw request body.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/content")]
public sealed class ReplaceFileController(IDispatcher dispatcher, IAccessEvaluator access) : ControllerBase
{
    /// <summary>Stores the body as the file's next version. Its id, name, folder and alternative text stay.</summary>
    [HttpPut]
    [RequireCapability(MediaCapabilities.DeleteOwn)]
    [UploadSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ReplaceAsync(
        Guid siteId,
        Guid fileId,
        [FromQuery] int expectedContentVersion,
        CancellationToken cancellationToken)
    {
        var command = new ReplaceFileCommand(
            siteId,
            fileId,
            access.UserId ?? throw new InvalidOperationException("Replace reached without a signed-in user."),
            expectedContentVersion,
            Request.Headers[UploadFileController.FileNameHeader].FirstOrDefault(),
            Request.ContentType,
            Request.Body,
            Request.PathBase.Value ?? string.Empty);

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
