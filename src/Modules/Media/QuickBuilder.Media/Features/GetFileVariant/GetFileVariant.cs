using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.GetFileVariant;

internal sealed record GetFileVariantQuery(Guid SiteId, Guid FileId, int Width, int Version) : IQuery<Result<AdminVariantContent>>;

/// <summary>A variant, or the original in its place, ready to send to the admin shell.</summary>
internal sealed record AdminVariantContent(MediaFile File, VariantBytes Bytes, MediaAddress Address);

/// <summary>
/// A resized preview for the admin shell (013 research MV-16, contracts/media-api.yaml
/// <c>getMediaFileVariant</c>).
/// </summary>
/// <remarks>
/// Works for sites with no host name, so the library, the picker and the editor canvas can
/// show variants everywhere. The query names the content version, so each address still
/// serves one set of bytes and can be cached privately for a year.
/// </remarks>
internal sealed class GetFileVariantHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    VariantResponder variants) : IQueryHandler<GetFileVariantQuery, Result<AdminVariantContent>>
{
    public async Task<Result<AdminVariantContent>> HandleAsync(GetFileVariantQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<AdminVariantContent>(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin variant of a file on a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == query.FileId && f.SiteId == query.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null || file.ContentVersion != query.Version)
        {
            return Result.Failure<AdminVariantContent>(MediaErrors.NotFound);
        }

        var bytes = await variants.GetAsync(file, query.Width, cancellationToken).ConfigureAwait(false);
        return bytes is null
            ? Result.Failure<AdminVariantContent>(MediaErrors.NotFound)
            : Result.Success(new AdminVariantContent(file, bytes, MediaAddress.Variant(file, query.Width)));
    }
}

/// <summary>Administrative surface: a file's resized preview.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/variants/{width:int}")]
public sealed class GetFileVariantController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The variant at <c>width</c> of content version <c>v</c>.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid fileId, int width, [FromQuery] int v, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetFileVariantQuery(siteId, fileId, width, v), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MediaErrorResults.ToActionResult(result.Error);
        }

        var content = result.Value;
        var kind = content.Bytes.IsFallback ? MediaResponseKind.VariantFallback : MediaResponseKind.AdminVariant;
        var (etag, lastModified) = MediaResponseHeaders.Apply(Response, content.File, content.Address, kind);

        return File(content.Bytes.Content.Content, content.File.ContentType, lastModified, etag, enableRangeProcessing: true);
    }
}
