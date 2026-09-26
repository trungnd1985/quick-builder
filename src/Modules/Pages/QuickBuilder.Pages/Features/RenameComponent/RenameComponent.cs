using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.CreateComponent;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.RenameComponent;

/// <summary>A component's new name, and the <see cref="Version"/> the caller last saw.</summary>
public sealed record RenameComponentRequest(string Name, uint Version);

internal sealed record RenameComponentCommand(Guid SiteId, Guid ComponentId, string Name, uint Version)
    : ICommand<Result<ComponentResponse>>;

internal sealed class RenameComponentValidator : IValidator<RenameComponentCommand>
{
    public ValidationResult Validate(RenameComponentCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Component.NormaliseName(instance.Name) is null
            ? ValidationResult.Invalid(new ValidationFailure("name", PagesErrors.InvalidComponentName.Message))
            : ValidationResult.Valid();
    }
}

/// <summary>
/// Renames a component. Pages reference it by id, so none of them change (edge case
/// "A component renamed").
/// </summary>
internal sealed class RenameComponentHandler(
    PagesDbContext database,
    ComponentReads reads,
    PagesAccess access,
    TimeProvider clock) : ICommandHandler<RenameComponentCommand, Result<ComponentResponse>>
{
    public async Task<Result<ComponentResponse>> HandleAsync(RenameComponentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ComponentResponse>(PagesErrors.SiteNotFound);
        }

        var component = await database.ForSite<Component>(command.SiteId)
            .FirstOrDefaultAsync(c => c.Id == command.ComponentId, cancellationToken)
            .ConfigureAwait(false);

        if (component is null)
        {
            return Result.Failure<ComponentResponse>(PagesErrors.ComponentNotFound);
        }

        if (component.Version != command.Version)
        {
            return Result.Failure<ComponentResponse>(PagesErrors.OutOfDate);
        }

        var renamed = component.Rename(command.Name, access.Actor, clock);
        if (renamed.IsFailure)
        {
            return Result.Failure<ComponentResponse>(renamed.Error);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<ComponentResponse>(PagesErrors.OutOfDate);
        }
        catch (DbUpdateException ex) when (CreateComponentHandler.IsDuplicateName(ex))
        {
            return Result.Failure<ComponentResponse>(PagesErrors.DuplicateComponentName);
        }

        return Result.Success((await reads.OneAsync(command.SiteId, component.Id, cancellationToken).ConfigureAwait(false))!);
    }
}

/// <summary>Administrative surface: rename a component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}")]
public sealed class RenameComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameAsync(
        Guid siteId,
        Guid componentId,
        RenameComponentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new RenameComponentCommand(siteId, componentId, request.Name, request.Version),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
