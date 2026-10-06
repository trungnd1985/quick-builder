using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.GetFile;

internal sealed record GetFileQuery(Guid SiteId, Guid FileId, string PathBase) : IQuery<Result<MediaFileDto>>;

/// <summary>One file with its details and usage summary (013 contracts/media-api.yaml <c>getMediaFile</c>).</summary>
internal sealed class GetFileHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    MediaFileMapper mapper) : IQueryHandler<GetFileQuery, Result<MediaFileDto>>
{
    public async Task<Result<MediaFileDto>> HandleAsync(GetFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin read of a file on a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == query.FileId && f.SiteId == query.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.NotFound);
        }

        var mapped = await mapper.MapAsync([file], query.SiteId, query.PathBase, checkAvailability: true, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(mapped[0]);
    }
}

/// <summary>Administrative surface: one file's details.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}")]
public sealed class GetFileController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The file, with its folder, alternative text, dimensions, versions and usage summary.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .QueryAsync(new GetFileQuery(siteId, fileId, Request.PathBase.Value ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
