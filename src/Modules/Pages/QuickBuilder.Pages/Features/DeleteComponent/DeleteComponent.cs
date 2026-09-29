using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.DeleteComponent;

internal sealed record DeleteComponentCommand(Guid SiteId, Guid ComponentId, uint Version) : ICommand<Result>;

/// <summary>
/// Permanently deletes a component that no page draft uses (FR-028, research P-11).
/// </summary>
/// <remarks>
/// The usage count is checked first so the refusal can say how many pages use it. The
/// foreign key from <c>component_usages</c> is what actually decides: if a draft starts
/// using the component between the count and the delete, the database refuses the delete,
/// and the caller gets the same answer with the new count.
/// </remarks>
internal sealed class DeleteComponentHandler(
    PagesDbContext database,
    PagesAccess access,
    PublishingWrites publishing,
    TimeProvider clock,
    ILogger<DeleteComponentHandler> logger)
    : ICommandHandler<DeleteComponentCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteComponentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(PagesErrors.SiteNotFound);
        }

        var component = await database.ForSite<Component>(command.SiteId)
            .FirstOrDefaultAsync(c => c.Id == command.ComponentId, cancellationToken)
            .ConfigureAwait(false);

        if (component is null)
        {
            return Result.Failure(PagesErrors.ComponentNotFound);
        }

        if (component.Version != command.Version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        var usages = await UsageCountAsync(command, cancellationToken).ConfigureAwait(false);
        if (usages > 0)
        {
            return Result.Failure(PagesErrors.ComponentInUse(usages));
        }

        // 012 FR-004: never take away a component published pages still show.
        var livePages = await publishing.LivePagesUsingAsync(command.SiteId, command.ComponentId, cancellationToken).ConfigureAwait(false);
        if (livePages > 0)
        {
            return Result.Failure(PagesErrors.ComponentInUseByPublishedPages(livePages));
        }

        // Its publishing records and the component go together, or not at all.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await publishing.ForgetAsync(
                new PublishingWrites.Subject(SubjectKinds.Component, component.Id, command.SiteId, component.Name, Path: null),
                access.Actor,
                cancellationToken)
            .ConfigureAwait(false);

        component.MarkDeleted(clock);
        PagesOutbox.AddFrom(database, component, access.Actor);
        database.Components.Remove(component);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // A page draft started using it after we counted (P-11).
            database.ChangeTracker.Clear();
            return Result.Failure(PagesErrors.ComponentInUse(await UsageCountAsync(command, cancellationToken).ConfigureAwait(false)));
        }

        logger.LogInformation("Component {ComponentId} deleted from site {SiteId}.", command.ComponentId, command.SiteId);
        return Result.Success();
    }

    private Task<int> UsageCountAsync(DeleteComponentCommand command, CancellationToken cancellationToken) =>
        database.ForSite<ComponentUsage>(command.SiteId).CountAsync(u => u.ComponentId == command.ComponentId, cancellationToken);
}

/// <summary>Administrative surface: delete a component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}")]
public sealed class DeleteComponentController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Deletes the component. Permanent: there is no restore.</summary>
    [HttpDelete]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid componentId, [FromQuery] uint version, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteComponentCommand(siteId, componentId, version), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
