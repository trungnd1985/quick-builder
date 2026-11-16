using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Trash;

internal sealed record ComponentRestorePlanQuery(Guid SiteId, Guid ComponentId) : IQuery<Result<RestorePlanResponse>>;

internal sealed record RestoreComponentCommand(Guid SiteId, Guid ComponentId, RestoreRequest Request) : ICommand<Result<RestoreResultResponse>>;

/// <summary>
/// Plans and performs a component's restore (021 FR-014, FR-015, FR-017, research TR-10, TR-11).
/// </summary>
/// <remarks>
/// A component has no place, only a name, which another component may have taken while it was
/// in the trash; it then comes back under a numbered name, shown in the plan first. It comes
/// back unpublished unless the member chose "Restore and publish".
/// </remarks>
internal sealed class RestoreComponentHandler(
    PagesTrash trash,
    PagesAccess access,
    PagesDbContext database,
    PublishingWrites publishing,
    TimeProvider clock,
    ILogger<RestoreComponentHandler> logger)
    : IQueryHandler<ComponentRestorePlanQuery, Result<RestorePlanResponse>>, ICommandHandler<RestoreComponentCommand, Result<RestoreResultResponse>>
{
    public async Task<Result<RestorePlanResponse>> HandleAsync(ComponentRestorePlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestorePlanResponse>(PagesErrors.SiteNotFound);
        }

        var component = await trash.TrashedComponents(query.SiteId).AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ComponentId, cancellationToken).ConfigureAwait(false);
        return component is null
            ? Result.Failure<RestorePlanResponse>(PagesErrors.NotInTrash)
            : Result.Success(await trash.PlanComponentAsync(component, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<RestoreResultResponse>> HandleAsync(RestoreComponentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(PagesErrors.SiteNotFound);
        }

        if (request.Publish && !await trash.MayAsync(PublishingCapabilities.Manage, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(PagesErrors.Forbidden);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var component = await trash.TrashedComponents(command.SiteId)
            .FirstOrDefaultAsync(c => c.Id == command.ComponentId, cancellationToken).ConfigureAwait(false);
        if (component is null)
        {
            return Result.Failure<RestoreResultResponse>(PagesErrors.NotInTrash);
        }

        var plan = await trash.PlanComponentAsync(component, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(plan.Address, request.ConfirmedAddress, StringComparison.Ordinal))
        {
            return Result.Failure<RestoreResultResponse>(PagesErrors.PlanChanged(plan));
        }

        component.Restore(plan.Address, access.Actor, clock);
        PagesOutbox.AddFrom(database, component, access.Actor);
        var subject = new PublishingWrites.Subject(SubjectKinds.Component, component.Id, command.SiteId, component.Name, Path: null);
        await publishing.UnshelveAsync(subject, access.Actor, cancellationToken).ConfigureAwait(false);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A restore or purge of the same entry won (FR-020).
            return Result.Failure<RestoreResultResponse>(PagesErrors.NotInTrash);
        }
        catch (DbUpdateException)
        {
            // Another component took the name between the plan and the save.
            database.ChangeTracker.Clear();
            var fresh = await trash.TrashedComponents(command.SiteId).AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.ComponentId, cancellationToken).ConfigureAwait(false);
            return fresh is null
                ? Result.Failure<RestoreResultResponse>(PagesErrors.NotInTrash)
                : Result.Failure<RestoreResultResponse>(PagesErrors.PlanChanged(await trash.PlanComponentAsync(fresh, cancellationToken).ConfigureAwait(false)));
        }

        if (request.Publish)
        {
            var version = await trash.LastLiveVersionAsync(command.SiteId, SubjectKinds.Component, component.Id, cancellationToken).ConfigureAwait(false);
            var published = version is { } number
                ? await publishing.RollbackContentAsync(subject, number, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false)
                : Result.Failure(PagesErrors.VersionNotFound);
            if (published.IsFailure)
            {
                // TR-11: restore and publish are one confirmed step; neither happens.
                return Result.Failure<RestoreResultResponse>(published.Error);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Component {ComponentId} restored from the trash of site {SiteId} (published: {Published}).", command.ComponentId, command.SiteId, request.Publish);
        return Result.Success(new RestoreResultResponse([new RestoredResponse(TrashKinds.Component, component.Id, component.Name)], request.Publish));
    }
}

/// <summary>Administrative surface: restore a trashed component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/trash/components/{componentId:guid}")]
public sealed class RestoreComponentController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>What restoring the component would do (TR-10).</summary>
    [HttpGet("restore-plan")]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType<RestorePlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ComponentRestorePlanQuery(siteId, componentId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Restores the component, optionally publishing its last live version.</summary>
    [HttpPost("restore")]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType<RestoreResultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, Guid componentId, [FromBody] RestoreRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RestoreComponentCommand(siteId, componentId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
