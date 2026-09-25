using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Storage;
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
    string PathBase) : ICommand<Result<MediaFileDto>>;

/// <summary>
/// Stores one uploaded file in a site's library (003 US1, FR-006 to FR-011).
/// </summary>
/// <remarks>
/// The order matters and is the whole design:
/// <list type="number">
/// <item>The name must survive cleaning — it is what the library shows.</item>
/// <item>The type is decided by the first bytes, never by the name or the declared type
/// (M-07). Only then are those two checked for agreement with it (M-08).</item>
/// <item>The bytes are written — all or nothing, counted against the limit as they arrive
/// (M-04, M-09).</item>
/// <item>Only then is the row inserted. A file appears in the library, and is served, only
/// once its bytes are durably stored (FR-011). If the insert fails, the bytes are removed
/// straight away; if even that fails, the orphan sweep removes them later (FR-012).</item>
/// </list>
/// </remarks>
internal sealed class UploadFileHandler(
    MediaDbContext database,
    IFileStorage storage,
    ISiteDirectory sites,
    IOptions<MediaOptions> options,
    MediaFileMapper mapper,
    TimeProvider clock,
    ILogger<UploadFileHandler> logger) : ICommandHandler<UploadFileCommand, Result<MediaFileDto>>
{
    public async Task<Result<MediaFileDto>> HandleAsync(UploadFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var settings = options.Value;

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.SiteNotFound);
        }

        var fileName = MediaFileName.Clean(command.RawFileName);
        if (fileName is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.FileNameRequired);
        }

        var head = new byte[FileTypeDetector.HeadLength];
        int headLength;
        try
        {
            headLength = await command.Body
                .ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.TooLarge(settings.MaxFileSizeBytes));
        }

        if (headLength == 0)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.Empty);
        }

        var type = FileTypeDetector.Detect(head.AsSpan(0, headLength));
        if (type is null || !settings.AllowedFileTypes.Contains(type))
        {
            return Result.Failure<MediaFileDto>(MediaErrors.UnsupportedType);
        }

        var extension = MediaFileName.Extension(fileName);
        if (!type.Accepts(extension, command.DeclaredType))
        {
            return Result.Failure<MediaFileDto>(MediaErrors.TypeMismatch);
        }

        var fileId = Guid.NewGuid();
        var key = MediaStorageKeys.For(command.SiteId, fileId);
        var upload = new UploadStream(head.AsMemory(0, headLength), command.Body, settings.MaxFileSizeBytes);

        var stored = await StoreAsync(key, upload, settings.MaxFileSizeBytes, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return Result.Failure<MediaFileDto>(stored);
        }

        var file = MediaFile.Create(
            fileId,
            command.SiteId,
            fileName,
            type,
            extension,
            upload.BytesRead,
            clock.GetUtcNow(),
            command.UserId,
            key.Value);

        database.MediaFiles.Add(file);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RemoveBytesAsync(key, command.SiteId, fileId).ConfigureAwait(false);
            throw;
        }

        // Identifiers only: a file's name can itself be personal data (FR-030).
        logger.LogInformation(
            "Media file {MediaFileId} ({ContentType}, {SizeBytes} bytes) added to site {SiteId}.",
            fileId, type.Mime, upload.BytesRead, command.SiteId);

        var dto = await mapper.MapAsync([file], command.SiteId, command.PathBase, checkAvailability: false, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(dto[0]);
    }

    /// <summary>Writes the bytes. Null on success, otherwise the error to report.</summary>
    private async Task<Error?> StoreAsync(StorageKey key, UploadStream upload, long limit, CancellationToken cancellationToken)
    {
        try
        {
            await storage.WriteAsync(key, upload, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (UploadTooLargeException)
        {
            return MediaErrors.TooLarge(limit);
        }
        catch (UploadInterruptedException)
        {
            // The client went away; nothing was stored and nobody is listening for an answer.
            throw;
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            logger.LogError(ex, "Storage is out of space; an upload to key prefix {KeyPrefix} was refused.", key.FirstSegment);
            return MediaErrors.StorageFull;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Storage failed while writing an upload for site {SiteId}.", key.FirstSegment);
            return MediaErrors.StorageUnavailable;
        }
    }

    private async Task RemoveBytesAsync(StorageKey key, Guid siteId, Guid fileId)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "Could not remove the bytes of media file {MediaFileId} on site {SiteId} after its record failed to save; " +
                "the orphan sweep will remove them.",
                fileId, siteId);
        }
    }

    /// <summary>ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL on Windows; ENOSPC / EDQUOT elsewhere.</summary>
    internal static bool IsDiskFull(IOException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        const int win32Prefix = unchecked((int)0x80070000);
        var hresult = ex.HResult;

        return (hresult & unchecked((int)0xFFFF0000)) == win32Prefix
            ? (hresult & 0xFFFF) is 0x70 or 0x27
            : hresult is 28 or 122;
    }
}

/// <summary>Administrative surface: upload one file, as the raw request body (research M-10).</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files")]
public sealed class UploadFileController(IDispatcher dispatcher, IAccessEvaluator access) : ControllerBase
{
    /// <summary>The header carrying the original file name, percent-encoded UTF-8.</summary>
    public const string FileNameHeader = "X-QuickBuilder-File-Name";

    /// <summary>Stores the request body as a new file in the site's library.</summary>
    [HttpPost]
    [RequireCapability(MediaCapabilities.Upload)]
    [UploadSizeLimit]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var command = new UploadFileCommand(
            siteId,
            access.UserId ?? throw new InvalidOperationException("Upload reached without a signed-in user."),
            Request.Headers[FileNameHeader].FirstOrDefault(),
            Request.ContentType,
            Request.Body,
            Request.PathBase.Value ?? string.Empty);

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? MediaErrorResults.ToActionResult(result.Error)
            : Created($"/api/v1/media/sites/{siteId}/files/{result.Value.Id}/content", result.Value);
    }
}
