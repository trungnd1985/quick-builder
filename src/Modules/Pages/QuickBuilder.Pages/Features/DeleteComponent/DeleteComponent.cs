using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.Trash;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.DeleteComponent;

internal sealed record DeleteComponentCommand(Guid SiteId, Guid ComponentId, uint Version) : ICommand<Result<TrashReceiptResponse>>;

/// <summary>
/// Moves a component that no page draft uses to the site's trash (010 FR-028, research P-11;
/// 021 FR-001).
/// </summary>
/// <remarks>
/// The usage count is checked first so the refusal can say how many pages use it, and checked
/// again in the transaction after the trash is written, so a draft that started using it
/// meanwhile turns the delete into the same refusal. A live component is unpublished as part
/// of the trash (TR-07), but only when no live page shows it (012 FR-004). Its name is free
/// while it is in the trash (FR-004, TR-04).
/// </remarks>
internal sealed class DeleteComponentHandler(
    PagesDbContext database,
    PagesAccess access,
    PublishingWrites publishing,
    TrashRetention retention,
    TimeProvider clock,
    IEditingLocks locks,
    ILogger<DeleteComponentHandler> logger)
    : ICommandHandler<DeleteComponentCommand, Result<TrashReceiptResponse>>
{
    public async Task<Result<TrashReceiptResponse>> HandleAsync(DeleteComponentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.SiteNotFound);
        }

        var component = await database.ForSite<Component>(command.SiteId)
            .FirstOrDefaultAsync(c => c.Id == command.ComponentId, cancellationToken)
            .ConfigureAwait(false);

        if (component is null)
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.ComponentNotFound);
        }

        if (component.Version != command.Version)
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.OutOfDate);
        }

        // 021 FR-008, TR-09: not while another member is editing it.
        if (await locks.FindActiveHolderAsync(command.SiteId, EditingDocumentKinds.Component, command.ComponentId, access.Actor, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            var names = await access.NamesAsync([holder.UserId], cancellationToken).ConfigureAwait(false);
            return Result.Failure<TrashReceiptResponse>(PagesErrors.DeleteLockHeld(holder.UserId, names.GetValueOrDefault(holder.UserId) ?? string.Empty, holder.Since));
        }

        var usages = await UsageCountAsync(command, cancellationToken).ConfigureAwait(false);
        if (usages > 0)
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.ComponentInUse(usages));
        }

        // 012 FR-004: never take away a component published pages still show.
        var livePages = await publishing.LivePagesUsingAsync(command.SiteId, command.ComponentId, cancellationToken).ConfigureAwait(false);
        if (livePages > 0)
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.ComponentInUseByPublishedPages(livePages));
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var shelved = await publishing.ShelveAsync(
                new PublishingWrites.Subject(SubjectKinds.Component, component.Id, command.SiteId, component.Name, Path: null),
                access.Actor,
                cancellationToken)
            .ConfigureAwait(false);
        if (shelved.IsFailure)
        {
            return Result.Failure<TrashReceiptResponse>(shelved.Error);
        }

        component.MoveToTrash(access.Actor, shelved.Value.WasLive, shelved.Value.HadSchedule, clock);
        PagesOutbox.AddFrom(database, component, access.Actor);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.OutOfDate);
        }

        // A page draft started using it after we counted (P-11): nothing is trashed.
        var lateUsages = await UsageCountAsync(command, cancellationToken).ConfigureAwait(false);
        if (lateUsages > 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            database.ChangeTracker.Clear();
            return Result.Failure<TrashReceiptResponse>(PagesErrors.ComponentInUse(lateUsages));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var trashedAt = component.TrashedAtUtc!.Value;
        logger.LogInformation("Component {ComponentId} moved to the trash of site {SiteId}.", command.ComponentId, command.SiteId);
        return Result.Success(new TrashReceiptResponse(trashedAt, retention.PurgeOn(trashedAt)));
    }

    private Task<int> UsageCountAsync(DeleteComponentCommand command, CancellationToken cancellationToken) =>
        database.ForSite<ComponentUsage>(command.SiteId).CountAsync(u => u.ComponentId == command.ComponentId, cancellationToken);
}

/// <summary>Administrative surface: delete a component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}")]
public sealed class DeleteComponentController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Moves the component to the trash, where it can be restored until the receipt's date (021 FR-002).</summary>
    [HttpDelete]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType<TrashReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid componentId, [FromQuery] uint version, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteComponentCommand(siteId, componentId, version), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
