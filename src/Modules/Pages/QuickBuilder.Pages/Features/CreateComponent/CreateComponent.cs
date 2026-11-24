using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.CreateComponent;

/// <summary>A component's name.</summary>
public sealed record ComponentNameRequest(string Name);

internal sealed record CreateComponentCommand(Guid SiteId, string Name) : ICommand<Result<ComponentResponse>>;

internal sealed class CreateComponentValidator : IValidator<CreateComponentCommand>
{
    public ValidationResult Validate(CreateComponentCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Component.NormaliseName(instance.Name) is null
            ? ValidationResult.Invalid(new ValidationFailure("name", PagesErrors.InvalidComponentName.Message))
            : ValidationResult.Valid();
    }
}

/// <summary>Creates a shared component with an empty draft (FR-024, FR-025).</summary>
/// <remarks>
/// Names are unique per site ignoring case. The unique index decides, so two people creating
/// "Header" at once cannot both succeed.
/// </remarks>
internal sealed class CreateComponentHandler(
    PagesDbContext database,
    ComponentReads reads,
    PagesAccess access,
    TimeProvider clock) : ICommandHandler<CreateComponentCommand, Result<ComponentResponse>>
{
    public async Task<Result<ComponentResponse>> HandleAsync(CreateComponentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ComponentResponse>(PagesErrors.SiteNotFound);
        }

        var actor = access.Actor;
        var created = Component.Create(command.SiteId, command.Name, actor, clock);
        if (created.IsFailure)
        {
            return Result.Failure<ComponentResponse>(created.Error);
        }

        var component = created.Value;
        database.Components.Add(component);
        database.ComponentDrafts.Add(ComponentDraft.CreateEmpty(command.SiteId, component.Id, actor, clock));
        PagesOutbox.AddFrom(database, component, actor);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            return Result.Failure<ComponentResponse>(PagesErrors.DuplicateComponentName);
        }

        return Result.Success((await reads.OneAsync(command.SiteId, component.Id, cancellationToken).ConfigureAwait(false))!);
    }

    internal static bool IsDuplicateName(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_components_site_name",
        };
}

/// <summary>Administrative surface: create a component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components")]
public sealed class CreateComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, ComponentNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new CreateComponentCommand(siteId, request.Name), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? PagesErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"{Request.PathBase}/api/v1/pages/sites/{siteId}/components/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
