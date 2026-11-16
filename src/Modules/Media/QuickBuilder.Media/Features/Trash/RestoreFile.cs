using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Contracts.Media;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.Trash;

internal sealed record FileRestorePlanQuery(Guid SiteId, Guid FileId, string? Placement, Guid? FolderId) : IQuery<Result<RestorePlanResponse>>;

internal sealed record RestoreFileCommand(Guid SiteId, Guid FileId, RestoreRequest Request) : ICommand<Result<RestoreResultResponse>>;

/// <summary>
/// Plans and performs a file's restore (021 FR-013, FR-015, FR-016, research TR-10).
/// </summary>
/// <remarks>
/// The file comes back at its own address with its variants, because its bytes never left. A
/// file that published pages still show changes what visitors see the moment it is back, so
/// restoring it needs the publishing capability, as deleting it does (013 FR-016). The
/// <c>FileRestored</c> event makes those pages show it again (FR-026).
/// </remarks>
internal sealed class RestoreFileHandler(
    MediaTrash trash,
    MediaDbContext database,
    ISiteDirectory sites,
    IAccessEvaluator access,
    MediaUsage usage,
    TimeProvider clock,
    ILogger<RestoreFileHandler> logger)
    : IQueryHandler<FileRestorePlanQuery, Result<RestorePlanResponse>>, ICommandHandler<RestoreFileCommand, Result<RestoreResultResponse>>
{
    public async Task<Result<RestorePlanResponse>> HandleAsync(FileRestorePlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<RestorePlanResponse>(MediaErrors.SiteNotFound);
        }

        var file = await trash.TrashedFiles(query.SiteId).AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Result.Failure<RestorePlanResponse>(MediaErrors.NotInTrash);
        }

        if (!await trash.MayRestoreAsync(file, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestorePlanResponse>(MediaErrors.Forbidden);
        }

        var plan = await trash.PlanAsync(file, query.Placement, query.FolderId, cancellationToken).ConfigureAwait(false);
        return plan is null ? Result.Failure<RestorePlanResponse>(MediaErrors.InvalidPlacement) : Result.Success(plan);
    }

    public async Task<Result<RestoreResultResponse>> HandleAsync(RestoreFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<RestoreResultResponse>(MediaErrors.SiteNotFound);
        }

        var file = await trash.TrashedFiles(command.SiteId).FirstOrDefaultAsync(f => f.Id == command.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Result.Failure<RestoreResultResponse>(MediaErrors.NotInTrash);
        }

        if (!await trash.MayRestoreAsync(file, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(MediaErrors.Forbidden);
        }

        // 013 FR-016: bringing back a file live pages show is a change visitors see.
        var used = (await usage.SummariseAsync(command.SiteId, [file.Id], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(file.Id) ?? MediaUsageSummary.None;
        if (used.InLiveUse
            && !await access.HasCapabilityAsync(PublishingCapabilities.Manage, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(MediaErrors.InLiveUse);
        }

        var plan = await trash.PlanAsync(file, request.Placement, request.FolderId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return Result.Failure<RestoreResultResponse>(MediaErrors.InvalidPlacement);
        }

        if (!string.Equals(plan.Address, request.ConfirmedAddress, StringComparison.Ordinal))
        {
            return Result.Failure<RestoreResultResponse>(MediaErrors.PlanChanged);
        }

        file.Restore(plan.FolderId);
        database.OutboxMessages.Add(OutboxMessage.From(
            new FileRestored(Guid.CreateVersion7(), clock.GetUtcNow(), file.SiteId, file.Id, access.UserId)));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A restore or purge of the same file won (FR-020).
            return Result.Failure<RestoreResultResponse>(MediaErrors.NotInTrash);
        }
        catch (DbUpdateException)
        {
            // The chosen folder was deleted between the plan and the save.
            return Result.Failure<RestoreResultResponse>(MediaErrors.PlanChanged);
        }

        logger.LogInformation("Media file {MediaFileId} restored from the trash of site {SiteId} by {ActorUserId}.", file.Id, file.SiteId, access.UserId);
        return Result.Success(new RestoreResultResponse([new RestoredResponse(TrashKinds.File, file.Id, plan.Address)], Published: false));
    }
}

/// <summary>Administrative surface: restore a trashed file.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/trash/files/{fileId:guid}")]
public sealed class RestoreFileController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Where restoring the file would put it (TR-10).</summary>
    [HttpGet("restore-plan")]
    [RequireCapability(MediaCapabilities.DeleteOwn)]
    [ProducesResponseType<RestorePlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanAsync(Guid siteId, Guid fileId, [FromQuery] string? placement, [FromQuery] Guid? folderId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new FileRestorePlanQuery(siteId, fileId, placement, folderId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Restores the file as planned.</summary>
    [HttpPost("restore")]
    [RequireCapability(MediaCapabilities.DeleteOwn)]
    [ProducesResponseType<RestoreResultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, Guid fileId, [FromBody] RestoreRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RestoreFileCommand(siteId, fileId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
