using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.GetFileContent;

/// <summary>A library row and its opened bytes, ready to serve.</summary>
internal sealed record MediaContent(MediaFile File, StoredFile Stored);

internal sealed record GetFileContentQuery(Guid SiteId, Guid FileId) : IQuery<Result<MediaContent>>;

/// <summary>
/// Opens a file for an admin preview (003 FR-018, research M-12).
/// </summary>
/// <remarks>
/// Works for sites with no host name, which is why previews cannot use the public address.
/// The site is named in the route, as every admin operation does (001 FR-011b).
/// </remarks>
internal sealed class GetFileContentHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    IFileStorage storage,
    ILogger<GetFileContentHandler> logger) : IQueryHandler<GetFileContentQuery, Result<MediaContent>>
{
    public async Task<Result<MediaContent>> HandleAsync(GetFileContentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaContent>(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin preview of a file on a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == query.FileId && f.SiteId == query.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return Result.Failure<MediaContent>(MediaErrors.NotFound);
        }

        var stored = await storage.OpenReadAsync(new StorageKey(file.StorageKey), cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            logger.LogWarning("Media file {MediaFileId} on site {SiteId} has no stored bytes.", file.Id, file.SiteId);
            return Result.Failure<MediaContent>(MediaErrors.NotFound);
        }

        return Result.Success(new MediaContent(file, stored));
    }
}

/// <summary>Administrative surface: a file's bytes, for previews in the admin shell.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/content")]
public sealed class GetFileContentController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The file's bytes, with the same safety headers as public serving.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetFileContentQuery(siteId, fileId), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MediaErrorResults.ToActionResult(result.Error);
        }

        var (file, stored) = result.Value;
        var (etag, lastModified) = MediaResponseHeaders.Apply(Response, file, MediaResponseKind.Preview);

        // FileStreamResult disposes the stream once the response is written.
        return File(stored.Content, file.ContentType, lastModified, etag, enableRangeProcessing: true);
    }
}
