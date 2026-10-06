using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Features.UploadFile;

/// <summary>New bytes that passed every check and are durably stored, but not yet recorded.</summary>
internal sealed record StoredUpload(
    string FileName,
    MediaFileType Type,
    string Extension,
    long SizeBytes,
    StorageKey Key,
    ImageMetadata? Metadata,
    MediaMetadataState MetadataState);

/// <summary>
/// Validates and stores one incoming file, for upload and replace alike (003 FR-006 to FR-011;
/// 013 research MV-07, MV-10).
/// </summary>
/// <remarks>
/// The order matters and is the whole design:
/// <list type="number">
/// <item>The name must survive cleaning — it is what the library shows.</item>
/// <item>The type is decided by the first bytes, never by the name or the declared type
/// (003 M-07). Only then are those two checked for agreement with it (M-08).</item>
/// <item>The bytes are written — all or nothing, counted against the limit as they arrive
/// (M-04, M-09).</item>
/// <item>For images, the header is read back from storage and checked against the pixel
/// limit before anything is recorded (013 MV-10). A refused image's bytes are removed.</item>
/// </list>
/// The caller records the row, and on failure calls <see cref="RemoveAsync"/>. If even that
/// fails, the orphan sweep removes the bytes later (FR-012).
/// </remarks>
internal sealed class UploadPipeline(
    IFileStorage storage,
    IOptions<MediaOptions> options,
    UploadGate gate,
    IImageMetadataProbe metadata,
    ILogger<UploadPipeline> logger)
{
    public async Task<Result<StoredUpload>> StoreAsync(
        StorageKey key,
        string? rawFileName,
        string? declaredType,
        Stream body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var settings = options.Value;

        var fileName = MediaFileName.Clean(rawFileName);
        if (fileName is null)
        {
            return Result.Failure<StoredUpload>(MediaErrors.FileNameRequired);
        }

        var head = new byte[FileTypeDetector.HeadLength];
        int headLength;
        try
        {
            headLength = await body
                .ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Result.Failure<StoredUpload>(MediaErrors.TooLarge(settings.MaxFileSizeBytes));
        }

        if (headLength == 0)
        {
            return Result.Failure<StoredUpload>(MediaErrors.Empty);
        }

        var type = FileTypeDetector.Detect(head.AsSpan(0, headLength));
        if (type is null || !settings.AllowedFileTypes.Contains(type))
        {
            return Result.Failure<StoredUpload>(MediaErrors.UnsupportedType);
        }

        var extension = MediaFileName.Extension(fileName);
        if (!type.Accepts(extension, declaredType))
        {
            return Result.Failure<StoredUpload>(MediaErrors.TypeMismatch);
        }

        var upload = new UploadStream(head.AsMemory(0, headLength), body, settings.MaxFileSizeBytes);

        // At most ProcessorCount - 1 uploads write at once, so a burst never takes every core
        // from the rest of the admin surface (SC-008, research M-23).
        Error? stored;
        using (await gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            stored = await WriteAsync(key, upload, settings.MaxFileSizeBytes, cancellationToken).ConfigureAwait(false);
        }

        if (stored is not null)
        {
            return Result.Failure<StoredUpload>(stored);
        }

        var image = new ProbedImage(null, MediaMetadataState.NotApplicable);
        if (type.IsImage)
        {
            var probed = await metadata.ProbeAsync(key, settings.MaxImagePixels, cancellationToken).ConfigureAwait(false);
            if (probed.IsFailure)
            {
                await RemoveAsync(key, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                return Result.Failure<StoredUpload>(probed.Error);
            }

            image = probed.Value;
        }

        return Result.Success(new StoredUpload(fileName, type, extension, upload.BytesRead, key, image.Metadata, image.State));
    }

    /// <summary>Removes stored bytes whose record could not be saved. Best effort (FR-012).</summary>
    public async Task RemoveAsync(StorageKey key, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "Could not remove stored bytes under key prefix {KeyPrefix} after their record failed to save; the orphan sweep will remove them.",
                key.FirstSegment);
        }
    }

    /// <summary>Writes the bytes. Null on success, otherwise the error to report.</summary>
    private async Task<Error?> WriteAsync(StorageKey key, UploadStream upload, long limit, CancellationToken cancellationToken)
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
        catch (StorageKeyExistsException)
        {
            // Only a replace can meet this: another replace of the same version got there first.
            return MediaErrors.Conflict;
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

/// <summary>
/// Reads a stored image's dimensions from its header, never decoding pixels (013 research
/// MV-10). Implemented over NetVips by <see cref="NetVipsImageMetadataProbe"/>.
/// </summary>
internal interface IImageMetadataProbe
{
    /// <summary>
    /// The image's displayed dimensions and metadata state (recorded, or unreadable: such a
    /// file is kept and served as uploaded, research MV-10 revised), or the refusal
    /// <c>media.dimensions_too_large</c> above <paramref name="maxPixels"/>.
    /// </summary>
    Task<Result<ProbedImage>> ProbeAsync(StorageKey key, long maxPixels, CancellationToken cancellationToken);
}
