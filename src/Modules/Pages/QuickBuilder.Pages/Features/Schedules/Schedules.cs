using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Schedules;

// 012 FR-018 to FR-021, research PR-14: scheduling a publish (pages and components) or an
// unpublish (pages). contracts/publishing-api.yaml, schedules/{action}. Each action has its
// own route: "action" is a route value MVC keeps for itself.

/// <summary>When, in UTC, and for a publish the draft version the member reviewed.</summary>
/// <param name="RedirectToPageId">017 FR-032, unpublish only: the page the address redirects to once it runs.</param>
public sealed record SetScheduleRequest(DateTimeOffset DueAtUtc, uint? DraftVersion, Guid? RedirectToPageId = null);

internal sealed record SetScheduleCommand(
    Guid SiteId, string SubjectKind, Guid SubjectId, string Action, DateTimeOffset DueAtUtc, uint? DraftVersion, Guid? RedirectToPageId = null)
    : ICommand<Result<ScheduleResponse>>;

internal sealed record CancelScheduleCommand(Guid SiteId, string SubjectKind, Guid SubjectId, string Action) : ICommand<Result>;

internal sealed class SetScheduleHandler(ScheduleWrites schedules) : ICommandHandler<SetScheduleCommand, Result<ScheduleResponse>>
{
    public Task<Result<ScheduleResponse>> HandleAsync(SetScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return schedules.SetAsync(
            command.SiteId, command.SubjectKind, command.SubjectId, command.Action, command.DueAtUtc, command.DraftVersion, command.RedirectToPageId, cancellationToken);
    }
}

internal sealed class CancelScheduleHandler(ScheduleWrites schedules) : ICommandHandler<CancelScheduleCommand, Result>
{
    public Task<Result> HandleAsync(CancelScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return schedules.CancelAsync(command.SiteId, command.SubjectKind, command.SubjectId, command.Action, cancellationToken);
    }
}

/// <summary>Administrative surface: schedule a page's publish or unpublish.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/schedules")]
public sealed class PageScheduleController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut("publish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetPublishAsync(Guid siteId, Guid pageId, SetScheduleRequest request, CancellationToken cancellationToken) =>
        Schedules.SetAsync(dispatcher, siteId, SubjectKinds.Page, pageId, ScheduleActions.Publish, request, cancellationToken);

    [HttpPut("unpublish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetUnpublishAsync(Guid siteId, Guid pageId, SetScheduleRequest request, CancellationToken cancellationToken) =>
        Schedules.SetAsync(dispatcher, siteId, SubjectKinds.Page, pageId, ScheduleActions.Unpublish, request, cancellationToken);

    [HttpDelete("publish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> CancelPublishAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken) =>
        Schedules.CancelAsync(dispatcher, siteId, SubjectKinds.Page, pageId, ScheduleActions.Publish, cancellationToken);

    [HttpDelete("unpublish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> CancelUnpublishAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken) =>
        Schedules.CancelAsync(dispatcher, siteId, SubjectKinds.Page, pageId, ScheduleActions.Unpublish, cancellationToken);
}

/// <summary>Administrative surface: schedule a shared component's publish. Components have no unpublish schedule.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/schedules")]
public sealed class ComponentScheduleController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut("publish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetPublishAsync(Guid siteId, Guid componentId, SetScheduleRequest request, CancellationToken cancellationToken) =>
        Schedules.SetAsync(dispatcher, siteId, SubjectKinds.Component, componentId, ScheduleActions.Publish, request, cancellationToken);

    [HttpDelete("publish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> CancelPublishAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken) =>
        Schedules.CancelAsync(dispatcher, siteId, SubjectKinds.Component, componentId, ScheduleActions.Publish, cancellationToken);
}

/// <summary>
/// Administrative surface: schedule a menu's publish (024 FR-004a). Like components, menus have
/// no unpublish schedule.
/// </summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/schedules")]
public sealed class MenuScheduleController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut("publish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetPublishAsync(Guid siteId, Guid menuId, SetScheduleRequest request, CancellationToken cancellationToken) =>
        Schedules.SetAsync(dispatcher, siteId, SubjectKinds.Menu, menuId, ScheduleActions.Publish, request, cancellationToken);

    [HttpDelete("publish")]
    [RequireCapability(PublishingCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> CancelPublishAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken) =>
        Schedules.CancelAsync(dispatcher, siteId, SubjectKinds.Menu, menuId, ScheduleActions.Publish, cancellationToken);
}

/// <summary>What the schedule controllers share.</summary>
internal static class Schedules
{
    public static async Task<IActionResult> SetAsync(
        IDispatcher dispatcher, Guid siteId, string kind, Guid subjectId, string action, SetScheduleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new SetScheduleCommand(siteId, kind, subjectId, action, request.DueAtUtc, request.DraftVersion, request.RedirectToPageId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : new OkObjectResult(result.Value);
    }

    public static async Task<IActionResult> CancelAsync(
        IDispatcher dispatcher, Guid siteId, string kind, Guid subjectId, string action, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new CancelScheduleCommand(siteId, kind, subjectId, action), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : new NoContentResult();
    }
}
