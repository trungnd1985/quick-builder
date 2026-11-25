using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Features.GetFileContent;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Routing;

namespace QuickBuilder.Media.Features.ServePublicFile;

internal sealed record ServePublicFileQuery(string File) : IQuery<Result<PublicMediaContent>>;

/// <summary>What to send for a public media address, and how to cache it.</summary>
internal sealed record PublicMediaContent(MediaFile File, MediaAddress Address, StoredFile Stored, MediaResponseKind Kind);

/// <summary>
/// Serves a media file on its site's public surface (003 US2, FR-013, FR-014; 013
/// contracts/public-media.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The lookup goes through the site-filtered context — no <c>AcrossAllSites</c> — so the
/// site resolved from the Host header decides the result, and another site's file simply
/// does not exist here (Principle V). Every miss, whatever its cause, is the same
/// <c>media.not_found</c>, so a response reveals nothing about other sites, about which ids
/// were ever issued, or about a file's earlier versions.
/// </para>
/// <para>
/// Only canonical addresses parse (<see cref="MediaAddress"/>), and the version and extension
/// must be the file's current ones: an address from before a replacement is not found from
/// the moment the replacement commits, on every instance, because every request reads the
/// row (013 FR-018, SC-009).
/// </para>
/// </remarks>
internal sealed class ServePublicFileHandler(
    MediaDbContext database,
    IFileStorage storage,
    VariantResponder variants,
    IOptions<MediaOptions> options,
    ILogger<ServePublicFileHandler> logger) : IQueryHandler<ServePublicFileQuery, Result<PublicMediaContent>>
{
    public async Task<Result<PublicMediaContent>> HandleAsync(ServePublicFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (MediaAddress.TryParse(query.File, options.Value.Variants.WidthSet) is not { } address)
        {
            return Result.Failure<PublicMediaContent>(MediaErrors.NotFound);
        }

        var file = await database.MediaFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == address.FileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null
            || file.ContentVersion != address.Version
            || !string.Equals(file.Extension, address.Extension, StringComparison.Ordinal))
        {
            return Result.Failure<PublicMediaContent>(MediaErrors.NotFound);
        }

        if (address.Width is { } width)
        {
            // 013 MV-12: produced once, then served from storage; the original, uncached, when
            // it cannot be produced; not found for a width this file does not have.
            var variant = await variants.GetAsync(file, width, cancellationToken).ConfigureAwait(false);
            return variant is null
                ? Result.Failure<PublicMediaContent>(MediaErrors.NotFound)
                : Result.Success(new PublicMediaContent(
                    file, address, variant.Content, variant.IsFallback ? MediaResponseKind.VariantFallback : MediaResponseKind.Public));
        }

        var stored = await storage.OpenReadAsync(new StorageKey(file.StorageKey), cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            logger.LogWarning("Media file {MediaFileId} on site {SiteId} has no stored bytes.", file.Id, file.SiteId);
            return Result.Failure<PublicMediaContent>(MediaErrors.NotFound);
        }

        return Result.Success(new PublicMediaContent(file, address, stored, MediaResponseKind.Public));
    }
}

/// <summary>Public surface: a media file on its site's own host names.</summary>
[ApiController]
// Public surface: site visitors never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class ServePublicFileController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The bytes, with the safety and caching headers of 013 contracts/public-media.md §1.3.</summary>
    // A catch-all, so that every path under /_media — including ones with extra or empty
    // segments — gets the same media.not_found rather than falling through (US2 scenario 6).
    [HttpGet(PublicRoutes.Media + "/{**file}")]
    [HttpHead(PublicRoutes.Media + "/{**file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string? file, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ServePublicFileQuery(file ?? string.Empty), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MediaErrorResults.ToActionResult(result.Error);
        }

        var content = result.Value;
        var (etag, lastModified) = MediaResponseHeaders.Apply(Response, content.File, content.Address, content.Kind);

        // FileStreamResult answers HEAD without a body and disposes the stream when done.
        return File(content.Stored.Content, content.File.ContentType, lastModified, etag, enableRangeProcessing: true);
    }
}
