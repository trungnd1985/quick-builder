using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.UpdateFile;

internal sealed record UpdateFileCommand(Guid SiteId, Guid FileId, string? AltText, string PathBase) : ICommand<Result<MediaFileDto>>;

/// <summary>
/// Sets or clears an image's alternative text (013 FR-008, research MV-04).
/// </summary>
/// <remarks>
/// Pages never read library text: the editor copies it into an Image block when a file is
/// chosen (FR-009). So this changes no page and sends no event. The row version turns an
/// edit of a row changed meanwhile (replaced, say) into <c>media.conflict</c>.
/// </remarks>
internal sealed class UpdateFileHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    MediaFileMapper mapper,
    ILogger<UpdateFileHandler> logger) : ICommandHandler<UpdateFileCommand, Result<MediaFileDto>>
{
    public async Task<Result<MediaFileDto>> HandleAsync(UpdateFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.SiteNotFound);
        }

        var file = await database.MediaFiles
            .AcrossAllSites("Admin edit of a file on a site named explicitly in the route (001 FR-011b).")
            .FirstOrDefaultAsync(f => f.Id == command.FileId && f.SiteId == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.NotFound);
        }

        var changed = file.SetAltText(command.AltText);
        if (changed.IsFailure)
        {
            return Result.Failure<MediaFileDto>(changed.Error);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<MediaFileDto>(MediaErrors.Conflict);
        }

        // Ids only: alternative text is content (FR-041).
        logger.LogInformation("Alternative text of media file {MediaFileId} on site {SiteId} changed.", file.Id, file.SiteId);

        var mapped = await mapper.MapAsync([file], command.SiteId, command.PathBase, checkAvailability: false, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(mapped[0]);
    }
}

/// <summary>The request body for changing a file's details.</summary>
public sealed record UpdateFileRequest(string? AltText);

/// <summary>Administrative surface: change a file's alternative text.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}")]
public sealed class UpdateFileController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Sets <c>altText</c>; null or empty clears it. Documents refuse any text.</summary>
    [HttpPatch]
    [RequireCapability(MediaCapabilities.Upload)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid siteId, Guid fileId, [FromBody] UpdateFileRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await dispatcher
            .SendAsync(new UpdateFileCommand(siteId, fileId, body.AltText, Request.PathBase.Value ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
