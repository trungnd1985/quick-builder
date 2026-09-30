using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.UploadFile;

internal sealed record UploadFileCommand(
    Guid SiteId,
    Guid UserId,
    string? RawFileName,
    string? DeclaredType,
    Stream Body,
    string PathBase,
    Guid? FolderId = null) : ICommand<Result<MediaFileDto>>;

/// <summary>
/// Stores one uploaded file in a site's library (003 US1, FR-006 to FR-011; 013 FR-010, FR-027).
/// </summary>
/// <remarks>
/// <see cref="UploadPipeline"/> validates and stores the bytes; this records them. A file
/// appears in the library, and is served, only once its bytes are durably stored (FR-011). If
/// the insert fails, the bytes are removed straight away; if even that fails, the orphan sweep
/// removes them later (FR-012). The folder, when given, is checked before any byte is stored.
/// </remarks>
internal sealed class UploadFileHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    UploadPipeline pipeline,
    MediaFileMapper mapper,
    ThumbnailQueue thumbnails,
    TimeProvider clock,
    ILogger<UploadFileHandler> logger) : ICommandHandler<UploadFileCommand, Result<MediaFileDto>>
{
    public async Task<Result<MediaFileDto>> HandleAsync(UploadFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.SiteNotFound);
        }

        if (command.FolderId is { } folderId
            && !await database.Folders
                .AcrossAllSites("Admin upload to a site named explicitly in the route (001 FR-011b).")
                .AnyAsync(f => f.Id == folderId && f.SiteId == command.SiteId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure<MediaFileDto>(MediaErrors.FolderNotFound);
        }

        var fileId = Guid.NewGuid();
        var key = MediaStorageKeys.For(command.SiteId, fileId);

        var stored = await pipeline.StoreAsync(key, command.RawFileName, command.DeclaredType, command.Body, cancellationToken)
            .ConfigureAwait(false);
        if (stored.IsFailure)
        {
            return Result.Failure<MediaFileDto>(stored.Error);
        }

        var upload = stored.Value;
        var file = MediaFile.Create(
            fileId,
            command.SiteId,
            upload.FileName,
            upload.Type,
            upload.Extension,
            upload.SizeBytes,
            clock.GetUtcNow(),
            command.UserId,
            key.Value,
            command.FolderId,
            upload.Metadata);

        database.MediaFiles.Add(file);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: "fk_media_files_folder",
        })
        {
            // The folder was deleted while the bytes were arriving.
            await pipeline.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
            return Result.Failure<MediaFileDto>(MediaErrors.FolderNotFound);
        }
        catch
        {
            await pipeline.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // Identifiers only: a file's name can itself be personal data (FR-030).
        logger.LogInformation(
            "Media file {MediaFileId} ({ContentType}, {SizeBytes} bytes) added to site {SiteId}.",
            fileId, upload.Type.Mime, upload.SizeBytes, command.SiteId);

        // 013 MV-13: the thumbnail is produced right after, without holding up this answer.
        thumbnails.Enqueue(file);

        var dto = await mapper.MapAsync([file], command.SiteId, command.PathBase, checkAvailability: false, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(dto[0]);
    }
}

/// <summary>Administrative surface: upload one file, as the raw request body (research M-10).</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files")]
public sealed class UploadFileController(IDispatcher dispatcher, IAccessEvaluator access) : ControllerBase
{
    /// <summary>The header carrying the original file name, percent-encoded UTF-8.</summary>
    public const string FileNameHeader = "X-QuickBuilder-File-Name";

    /// <summary>Stores the request body as a new file in the site's library, optionally in a folder (013).</summary>
    [HttpPost]
    [RequireCapability(MediaCapabilities.Upload)]
    [UploadSizeLimit]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadAsync(Guid siteId, [FromQuery] Guid? folderId, CancellationToken cancellationToken)
    {
        var command = new UploadFileCommand(
            siteId,
            access.UserId ?? throw new InvalidOperationException("Upload reached without a signed-in user."),
            Request.Headers[FileNameHeader].FirstOrDefault(),
            Request.ContentType,
            Request.Body,
            Request.PathBase.Value ?? string.Empty,
            folderId);

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? MediaErrorResults.ToActionResult(result.Error)
            : Created($"/api/v1/media/sites/{siteId}/files/{result.Value.Id}/content", result.Value);
    }
}
