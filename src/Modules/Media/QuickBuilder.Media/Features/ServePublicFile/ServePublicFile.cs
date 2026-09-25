using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Features.GetFileContent;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Features.ServePublicFile;

internal sealed record ServePublicFileQuery(string File) : IQuery<Result<MediaContent>>;

/// <summary>
/// Serves a media file on its site's public surface (003 US2, FR-013, FR-014).
/// </summary>
/// <remarks>
/// The lookup goes through the site-filtered context — no <c>AcrossAllSites</c> — so the
/// site resolved from the Host header decides the result, and another site's file simply
/// does not exist here (Principle V). Every miss, whatever its cause, is the same
/// <c>media.not_found</c>, so a response reveals nothing about other sites or about which
/// ids were ever issued. The address is only the id and the stored extension: the file name
/// never appears in a path, so it never reaches an access log (FR-030).
/// </remarks>
internal sealed partial class ServePublicFileHandler(
    MediaDbContext database,
    IFileStorage storage,
    ILogger<ServePublicFileHandler> logger) : IQueryHandler<ServePublicFileQuery, Result<MediaContent>>
{
    public async Task<Result<MediaContent>> HandleAsync(ServePublicFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var address = Address().Match(query.File);
        if (!address.Success)
        {
            return Result.Failure<MediaContent>(MediaErrors.NotFound);
        }

        var fileId = Guid.ParseExact(address.Groups["id"].Value, "N");
        var extension = address.Groups["ext"].Value;

        var file = await database.MediaFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null || !string.Equals(file.Extension, extension, StringComparison.Ordinal))
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

    // Exactly what the server issues: 32 lower-case hex digits, a dot, a lower-case extension.
    // Anything else — traversal, encodings, upper case, hyphens — is a miss before any lookup.
    [GeneratedRegex("^(?<id>[0-9a-f]{32})\\.(?<ext>[a-z0-9]{1,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex Address();
}

/// <summary>Public surface: a media file on its site's own host names.</summary>
[ApiController]
// Public surface: site visitors never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class ServePublicFileController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The file's bytes, with the safety and caching headers of research M-11.</summary>
    // A catch-all, so that every path under /_media — including ones with extra or empty
    // segments — gets the same media.not_found rather than falling through (US2 scenario 6).
    [HttpGet("/_media/{**file}")]
    [HttpHead("/_media/{**file}")]
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

        var (media, stored) = result.Value;
        var (etag, lastModified) = MediaResponseHeaders.Apply(Response, media, MediaResponseKind.Public);

        // FileStreamResult answers HEAD without a body and disposes the stream when done.
        return File(stored.Content, media.ContentType, lastModified, etag, enableRangeProcessing: true);
    }
}
