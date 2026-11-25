using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.CopyPage;
using QuickBuilder.Pages.Features.CreateComponent;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.CopyComponent;

internal sealed record CopyComponentCommand(Guid SiteId, Guid ComponentId, JsonElement Document, DateTimeOffset BaseSavedAtUtc, string? Name)
    : ICommand<Result<ComponentResponse>>;

/// <summary>
/// Saves the member's version of a shared component as a new, unpublished component that no page
/// uses (023 FR-020 to FR-022). Its name is the member's localised one, with a number when
/// another component has it. The component and its draft commit together.
/// </summary>
internal sealed class CopyComponentHandler(
    DraftWrites drafts,
    ComponentReads reads,
    PagesAccess access,
    PagesDbContext database,
    TimeProvider clock)
    : ICommandHandler<CopyComponentCommand, Result<ComponentResponse>>
{
    public async Task<Result<ComponentResponse>> HandleAsync(CopyComponentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ComponentResponse>(PagesErrors.SiteNotFound);
        }

        var source = await database.ForSite<Component>(command.SiteId)
            .AsNoTracking()
            .Where(c => c.Id == command.ComponentId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (source is null)
        {
            return Result.Failure<ComponentResponse>(PagesErrors.ComponentNotFound);
        }

        var checkedCopy = await drafts.CheckCopyAsync(command.SiteId, command.Document, DocumentKind.Component, cancellationToken).ConfigureAwait(false);
        if (checkedCopy.IsFailure)
        {
            return Result.Failure<ComponentResponse>(checkedCopy.Error);
        }

        var names = await database.ForSite<Component>(command.SiteId).Select(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var name = CopyNaming.Unique(CopyNaming.Text(command.Name, Component.MaxNameLength, fallback: source.Name), names, Component.MaxNameLength);

        var actor = access.Actor;
        var created = Component.Create(command.SiteId, name, actor, clock);
        if (created.IsFailure)
        {
            return Result.Failure<ComponentResponse>(created.Error);
        }

        var component = created.Value;
        component.MarkCopyOf(new CopyOrigin(source.Id, command.BaseSavedAtUtc, actor, clock.GetUtcNow()));
        database.Components.Add(component);
        await drafts.StageCopyDraftAsync(command.SiteId, SubjectKinds.Component, component.Id, checkedCopy.Value, cancellationToken).ConfigureAwait(false);
        PagesOutbox.AddFrom(database, component, actor);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (CreateComponentHandler.IsDuplicateName(ex))
        {
            // Another component took the name a moment ago.
            return Result.Failure<ComponentResponse>(PagesErrors.DuplicateComponentName);
        }

        return Result.Success((await reads.OneAsync(command.SiteId, component.Id, cancellationToken).ConfigureAwait(false))!);
    }
}

/// <summary>Administrative surface: save a version of a component as a new component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/copies")]
public sealed class CopyComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [DraftSizeLimit]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyAsync(Guid siteId, Guid componentId, CopyDocumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new CopyComponentCommand(siteId, componentId, request.Document, request.BaseSavedAtUtc, request.Name), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? PagesErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"{Request.PathBase}/api/v1/pages/sites/{siteId}/components/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
