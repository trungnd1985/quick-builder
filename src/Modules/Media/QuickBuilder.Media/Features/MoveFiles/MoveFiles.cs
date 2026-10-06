using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.MoveFiles;

internal sealed record MoveFilesCommand(Guid SiteId, IReadOnlyList<Guid> FileIds, Guid? FolderId) : ICommand<Result>;

/// <summary>
/// Files up to 100 files in a folder, or at the top level (013 FR-002, FR-003).
/// </summary>
/// <remarks>
/// One set-based update, filtered to the route's site: ids of another site's files, or of
/// no file at all, change nothing and are not reported, so the answer reveals nothing about
/// them. Addresses never depend on folders, so nothing else changes and no event is sent.
/// Last move wins; a file is never in two folders or in none.
/// </remarks>
internal sealed class MoveFilesHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    ILogger<MoveFilesHandler> logger) : ICommandHandler<MoveFilesCommand, Result>
{
    internal const int MaxFiles = 100;

    public async Task<Result> HandleAsync(MoveFilesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FileIds.Count > MaxFiles)
        {
            return Result.Failure(MediaErrors.TooManyFiles);
        }

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure(MediaErrors.SiteNotFound);
        }

        if (command.FolderId is { } folderId
            && !await database.Folders
                .AcrossAllSites("Admin move on a site named explicitly in the route (001 FR-011b).")
                .AnyAsync(f => f.Id == folderId && f.SiteId == command.SiteId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(MediaErrors.FolderNotFound);
        }

        var ids = command.FileIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success();
        }

        int moved;
        try
        {
            moved = await database.MediaFiles
                .AcrossAllSites("Admin move on a site named explicitly in the route (001 FR-011b).")
                .Where(f => f.SiteId == command.SiteId && ids.Contains(f.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.FolderId, command.FolderId), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The folder was deleted between the check and the update.
            return Result.Failure(MediaErrors.FolderNotFound);
        }

        logger.LogInformation("Moved {Count} media file(s) on site {SiteId} to folder {FolderId}.", moved, command.SiteId, command.FolderId);
        return Result.Success();
    }
}

/// <summary>The request body for moving files.</summary>
public sealed record MoveFilesRequest(IReadOnlyList<Guid>? FileIds, Guid? FolderId);

/// <summary>Administrative surface: file media files in a folder (013 contracts/media-api.yaml).</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/move")]
public sealed class MoveFilesController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Moves the files into <c>folderId</c>, or to the top level when it is null or absent.</summary>
    [HttpPost]
    [RequireCapability(MediaCapabilities.Upload)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveAsync(Guid siteId, [FromBody] MoveFilesRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await dispatcher
            .SendAsync(new MoveFilesCommand(siteId, body.FileIds ?? [], body.FolderId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
