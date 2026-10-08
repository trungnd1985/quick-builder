using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items.RunItemSchedule;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.ScheduleItem;

internal sealed record ScheduleItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, string Action, ScheduleRequest Request) : ICommand<Result<ItemResponse>>;

internal sealed record CancelItemScheduleCommand(Guid SiteId, Guid CollectionId, Guid ItemId, string Action, int Revision) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Sets or moves an item's pending publish or unpublish (015 US6, FR-024, FR-025; research IT-09).
/// </summary>
/// <remarks>
/// A publish must pass the publish checks now, and captures the draft, so what goes live is what
/// was reviewed; moving it keeps the captured content unless <c>recapture</c> asks for the current
/// draft. An unpublish needs something published or about to be, and must come after a scheduled
/// publish. Each set or move writes a due message to the Cms outbox, available at the due time;
/// the schedule's revision turns earlier messages into no-ops.
/// </remarks>
internal sealed class ScheduleItemHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<ScheduleItemCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(ScheduleItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        if (!ItemScheduleActions.IsKnown(command.Action))
        {
            return Result.Failure<ItemResponse>(CmsErrors.ScheduleNotFound);
        }

        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, command.Request.Revision, null, access.Actor),
                context => SetAsync(context, command, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<Item?>> SetAsync(ItemWriteContext context, ScheduleItemCommand command, CancellationToken cancellationToken)
    {
        var item = context.Item!;
        var due = command.Request.DueAtUtc.ToUniversalTime();
        if (due <= context.Now)
        {
            return Result.Failure<Item?>(CmsErrors.ScheduleInPast);
        }

        var publish = await ItemPublishing.PendingAsync(context, ItemScheduleActions.Publish, cancellationToken).ConfigureAwait(false);
        var unpublish = await ItemPublishing.PendingAsync(context, ItemScheduleActions.Unpublish, cancellationToken).ConfigureAwait(false);
        ItemSchedule schedule;

        if (command.Action == ItemScheduleActions.Publish)
        {
            if (unpublish is not null && unpublish.DueAtUtc <= due)
            {
                return Result.Failure<Item?>(CmsErrors.ScheduleOrder);
            }

            var capturing = publish is null || command.Request.Recapture;
            if (capturing)
            {
                var report = await context.ValidateAsync(item.Id, item.Slug, item.DraftValues(), cancellationToken).ConfigureAwait(false);
                if (!report.IsEmpty)
                {
                    return Result.Failure<Item?>(CmsErrors.ItemNeedsAttention(ItemReads.Report(report)));
                }
            }

            if (publish is null)
            {
                schedule = ItemSchedule.ForPublish(context.SiteId, item.Id, due, item.DraftValues(), item.Slug, context.Actor, context.Now);
                context.Database.ItemSchedules.Add(schedule);
            }
            else
            {
                schedule = publish;
                schedule.Reschedule(due, context.Actor);
                if (command.Request.Recapture)
                {
                    schedule.Recapture(item.DraftValues(), item.Slug);
                }
            }

            item.RecordSchedule(publish is null ? ItemHistoryKinds.Scheduled : ItemHistoryKinds.Rescheduled, ItemScheduleActions.Publish, due, null, context.Actor, context.Now);
        }
        else
        {
            if (!item.IsPublished && publish is null)
            {
                return Result.Failure<Item?>(CmsErrors.ItemNotPublished);
            }

            if (publish is not null && due <= publish.DueAtUtc)
            {
                return Result.Failure<Item?>(CmsErrors.ScheduleOrder);
            }

            if (unpublish is null)
            {
                schedule = ItemSchedule.ForUnpublish(context.SiteId, item.Id, due, context.Actor, context.Now);
                context.Database.ItemSchedules.Add(schedule);
            }
            else
            {
                schedule = unpublish;
                schedule.Reschedule(due, context.Actor);
            }

            item.RecordSchedule(unpublish is null ? ItemHistoryKinds.Scheduled : ItemHistoryKinds.Rescheduled, ItemScheduleActions.Unpublish, due, null, context.Actor, context.Now);
        }

        context.Database.OutboxMessages.Add(OutboxMessage.From(
            new ItemScheduleDue(Guid.CreateVersion7(), context.Now, context.SiteId, schedule.Id, schedule.Revision),
            availableAtUtc: due));
        context.Changed(item);
        return Result.Success<Item?>(item);
    }
}

/// <summary>Cancels an item's pending publish or unpublish (015 US6, FR-024).</summary>
internal sealed class CancelItemScheduleHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<CancelItemScheduleCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(CancelItemScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, command.Revision, null, access.Actor),
                async context =>
                {
                    var pending = ItemScheduleActions.IsKnown(command.Action)
                        ? await ItemPublishing.PendingAsync(context, command.Action, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (pending is null)
                    {
                        return Result.Failure<Item?>(CmsErrors.ScheduleNotFound);
                    }

                    pending.Cancel(context.Now);
                    context.Item!.RecordSchedule(ItemHistoryKinds.ScheduleCancelled, command.Action, pending.DueAtUtc, null, context.Actor, context.Now);
                    context.Changed(context.Item);
                    return Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: an item's schedules.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/schedules/{scheduleAction}")]
public sealed class ItemSchedulesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(CmsCapabilities.ItemsPublish)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetAsync(Guid siteId, Guid collectionId, Guid itemId, string scheduleAction, ScheduleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new ScheduleItemCommand(siteId, collectionId, itemId, scheduleAction, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpDelete]
    [RequireCapability(CmsCapabilities.ItemsPublish)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(Guid siteId, Guid collectionId, Guid itemId, string scheduleAction, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new CancelItemScheduleCommand(siteId, collectionId, itemId, scheduleAction, revision), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
