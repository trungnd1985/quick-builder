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

namespace QuickBuilder.Media.Features.Folders;

// The four folder slices of 013 (contracts/media-api.yaml, data-model.md §3.2). They share
// FolderWrites, and they are small enough that one file reads better than four.

internal sealed record ListFoldersQuery(Guid SiteId) : IQuery<Result<MediaFolderTreeDto>>;

internal sealed record CreateFolderCommand(Guid SiteId, string? Name, Guid? ParentId, Guid UserId) : ICommand<Result<MediaFolderDto>>;

internal sealed record UpdateFolderCommand(Guid SiteId, Guid FolderId, string? Name, bool Move, Guid? ParentId)
    : ICommand<Result<MediaFolderDto>>;

internal sealed record DeleteFolderCommand(Guid SiteId, Guid FolderId) : ICommand<Result>;

/// <summary>The site's whole folder tree, with direct file counts (013 MV-02).</summary>
internal sealed class ListFoldersHandler(ISiteDirectory sites, FolderWrites folders, MediaDbContext database)
    : IQueryHandler<ListFoldersQuery, Result<MediaFolderTreeDto>>
{
    public async Task<Result<MediaFolderTreeDto>> HandleAsync(ListFoldersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFolderTreeDto>(MediaErrors.SiteNotFound);
        }

        var all = await database.Folders
            .AcrossAllSites("Admin folder tree of a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .Where(f => f.SiteId == query.SiteId)
            .OrderBy(f => f.NameKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var revision = await database.FolderTrees
            .AcrossAllSites("Admin folder tree of a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .Where(t => t.SiteId == query.SiteId)
            .Select(t => (long?)t.Revision)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        var (byFolder, topLevel) = await folders.FileCountsAsync(query.SiteId, cancellationToken).ConfigureAwait(false);

        return Result.Success(new MediaFolderTreeDto(
            [.. all.Select(f => FolderWrites.ToDto(f, byFolder.GetValueOrDefault(f.Id)))],
            topLevel,
            revision));
    }
}

/// <summary>Creates a folder at the top level or inside another (FR-001, FR-002, FR-004).</summary>
internal sealed class CreateFolderHandler(ISiteDirectory sites, FolderWrites folders, TimeProvider clock, ILogger<CreateFolderHandler> logger)
    : ICommandHandler<CreateFolderCommand, Result<MediaFolderDto>>
{
    public async Task<Result<MediaFolderDto>> HandleAsync(CreateFolderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFolderDto>(MediaErrors.SiteNotFound);
        }

        var structure = await folders.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        var created = structure.Create(Guid.CreateVersion7(), command.SiteId, command.ParentId, command.Name, clock.GetUtcNow(), command.UserId);
        if (created.IsFailure)
        {
            return Result.Failure<MediaFolderDto>(created.Error);
        }

        folders.Track(created.Value);
        var saved = await folders.SaveAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return Result.Failure<MediaFolderDto>(saved.Error);
        }

        // Ids only: a folder name is content (FR-041).
        logger.LogInformation("Media folder {FolderId} created on site {SiteId}.", created.Value.Id, command.SiteId);
        return Result.Success(FolderWrites.ToDto(created.Value, 0));
    }
}

/// <summary>Renames and/or moves a folder, whoever created it (clarification: folders have no owner).</summary>
internal sealed class UpdateFolderHandler(ISiteDirectory sites, FolderWrites folders, ILogger<UpdateFolderHandler> logger)
    : ICommandHandler<UpdateFolderCommand, Result<MediaFolderDto>>
{
    public async Task<Result<MediaFolderDto>> HandleAsync(UpdateFolderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaFolderDto>(MediaErrors.SiteNotFound);
        }

        var structure = await folders.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (structure.Find(command.FolderId) is not { } folder)
        {
            return Result.Failure<MediaFolderDto>(MediaErrors.FolderNotFound);
        }

        if (command.Name is not null)
        {
            var renamed = structure.Rename(folder, command.Name);
            if (renamed.IsFailure)
            {
                return Result.Failure<MediaFolderDto>(renamed.Error);
            }
        }

        if (command.Move)
        {
            var moved = structure.Move(folder, command.ParentId);
            if (moved.IsFailure)
            {
                return Result.Failure<MediaFolderDto>(moved.Error);
            }
        }

        var saved = await folders.SaveAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return Result.Failure<MediaFolderDto>(saved.Error);
        }

        var (byFolder, _) = await folders.FileCountsAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Media folder {FolderId} changed on site {SiteId}.", folder.Id, command.SiteId);
        return Result.Success(FolderWrites.ToDto(folder, byFolder.GetValueOrDefault(folder.Id)));
    }
}

/// <summary>Deletes an empty folder. The database refuses one that is not (FR-002).</summary>
internal sealed class DeleteFolderHandler(ISiteDirectory sites, FolderWrites folders, MediaDbContext database, ILogger<DeleteFolderHandler> logger)
    : ICommandHandler<DeleteFolderCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteFolderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure(MediaErrors.SiteNotFound);
        }

        var structure = await folders.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (structure.Find(command.FolderId) is not { } folder)
        {
            return Result.Failure(MediaErrors.FolderNotFound);
        }

        // A friendly answer for the common case; the RESTRICT foreign keys decide the race.
        if (structure.Descendants(folder.Id).Count > 1)
        {
            return Result.Failure(MediaErrors.FolderNotEmpty);
        }

        database.Folders.Remove(folder);
        var saved = await folders.SaveAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (saved.IsSuccess)
        {
            logger.LogInformation("Media folder {FolderId} deleted from site {SiteId}.", folder.Id, command.SiteId);
        }

        return saved;
    }
}

/// <summary>The request body for creating a folder.</summary>
public sealed record CreateFolderRequest(string? Name, Guid? ParentId);

/// <summary>Administrative surface: a site's folders (013 contracts/media-api.yaml).</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/folders")]
public sealed class FoldersController(IDispatcher dispatcher, IAccessEvaluator access) : ControllerBase
{
    /// <summary>The whole folder tree.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListFoldersQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Creates a folder.</summary>
    [HttpPost]
    [RequireCapability(MediaCapabilities.Upload)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, [FromBody] CreateFolderRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var command = new CreateFolderCommand(
            siteId,
            body.Name,
            body.ParentId,
            access.UserId ?? throw new InvalidOperationException("Folder creation reached without a signed-in user."));

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? MediaErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"{Request.PathBase}/api/v1/media/sites/{siteId}/folders/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}

/// <summary>Administrative surface: one folder (013 contracts/media-api.yaml).</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/folders/{folderId:guid}")]
public sealed class FolderController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Renames and/or moves the folder. A <c>parentId</c> member, even null, means "move".</summary>
    [HttpPatch]
    [RequireCapability(MediaCapabilities.Upload)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid siteId, Guid folderId, [FromBody] System.Text.Json.JsonElement body, CancellationToken cancellationToken)
    {
        if (body.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return MediaErrorResults.ToActionResult(MediaErrors.InvalidFolderName);
        }

        string? name = null;
        if (body.TryGetProperty("name", out var nameValue))
        {
            name = nameValue.ValueKind == System.Text.Json.JsonValueKind.String ? nameValue.GetString() ?? string.Empty : string.Empty;
        }

        var move = body.TryGetProperty("parentId", out var parentValue);
        Guid? parentId = null;
        if (move && parentValue.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            if (parentValue.ValueKind != System.Text.Json.JsonValueKind.String || !Guid.TryParse(parentValue.GetString(), out var parsed))
            {
                return MediaErrorResults.ToActionResult(MediaErrors.FolderNotFound);
            }

            parentId = parsed;
        }

        var result = await dispatcher
            .SendAsync(new UpdateFolderCommand(siteId, folderId, name, move, parentId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Deletes the folder, which must be empty.</summary>
    [HttpDelete]
    [RequireCapability(MediaCapabilities.Upload)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid folderId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteFolderCommand(siteId, folderId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
