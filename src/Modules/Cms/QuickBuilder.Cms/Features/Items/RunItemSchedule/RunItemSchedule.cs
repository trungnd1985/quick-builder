using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.RunItemSchedule;

/// <summary>
/// An item schedule's time has come (015 research IT-09). Module-internal: written to the Cms
/// outbox with <c>available_at_utc</c> at the due time, and handled only by Cms.
/// </summary>
/// <remarks>Carries the schedule's revision when written; moving the schedule makes this one a no-op.</remarks>
internal sealed record ItemScheduleDue(Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid ScheduleId, int Revision) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemScheduleDue";
}

/// <summary>
/// Runs a due item schedule (015 FR-026, SC-006), as 012's runner does for pages.
/// </summary>
/// <remarks>
/// <para>
/// The run is the same <see cref="ItemPublishing"/> operation as a manual publish or unpublish,
/// through <see cref="ItemWrites"/>, by the member who set the schedule, and the schedule is
/// completed in the same transaction. It runs once: the outbox gives the message to one
/// dispatcher, a message of an older revision finds the schedule no longer due, and a second
/// run that raced loses on the item's revision and the schedule's state, both concurrency tokens.
/// </para>
/// <para>
/// A refusal the rules make (the captured content now needs attention, nothing is published any
/// more) is a decision, not an error: the schedule is marked failed with the reason, recorded in
/// the history, and the item shows it needs attention; the outbox does not retry it. A schema
/// change holding the collection is not a decision: the handler throws, and the outbox retries
/// shortly, after the change.
/// </para>
/// </remarks>
internal sealed class RunItemScheduleHandler(IServiceScopeFactory scopes, ILogger<RunItemScheduleHandler> logger)
    : IIntegrationEventHandler<ItemScheduleDue>
{
    public async Task HandleAsync(ItemScheduleDue integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Guid itemId;
        Guid collectionId;
        Guid owner;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var schedule = await database.ForSite<ItemSchedule>(integrationEvent.SiteId)
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == integrationEvent.ScheduleId, cancellationToken)
                .ConfigureAwait(false);
            if (schedule is null || !schedule.IsDue(integrationEvent.Revision))
            {
                return;
            }

            var found = await database.ForSite<Item>(integrationEvent.SiteId)
                .AsNoTracking()
                .Where(i => i.Id == schedule.ItemId)
                .Select(i => new { i.Id, i.CollectionId })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (found is null)
            {
                return;
            }

            (itemId, collectionId, owner) = (found.Id, found.CollectionId, schedule.CreatedBy);
        }

        var ran = await RunAsync(integrationEvent, collectionId, itemId, owner, cancellationToken).ConfigureAwait(false);
        if (ran.IsSuccess)
        {
            return;
        }

        if (ran.Error.Code == CmsErrors.ItemsBeingUpdated.Code)
        {
            throw new InvalidOperationException("A schema change holds the collection; the outbox retries this schedule shortly.");
        }

        if (ran.Error.Code is "cms.item_conflict" or "cms.item_not_found")
        {
            // Another run, or a member's action on the item, got there first: if the schedule is
            // still due, the outbox delivers again; otherwise there is nothing left to do.
            if (await StillDueAsync(integrationEvent, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The item changed while its schedule ran; the outbox retries it.");
            }

            return;
        }

        await FailAsync(integrationEvent, collectionId, itemId, owner, ran.Error.Code, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<Item?>> RunAsync(ItemScheduleDue due, Guid collectionId, Guid itemId, Guid owner, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var writes = scope.ServiceProvider.GetRequiredService<ItemWrites>();
        return await writes.ExecuteAsync(
                new ItemWriteRequest(due.SiteId, collectionId, itemId, null, null, owner),
                async context =>
                {
                    var schedule = await context.Database.ForSite<ItemSchedule>(due.SiteId)
                        .SingleOrDefaultAsync(s => s.Id == due.ScheduleId, cancellationToken)
                        .ConfigureAwait(false);
                    if (schedule is null || !schedule.IsDue(due.Revision))
                    {
                        return Result.Success<Item?>(context.Item);
                    }

                    Result outcome = schedule.Action == ItemScheduleActions.Publish
                        ? (Result)await ItemPublishing.PublishAsync(
                                context, schedule.ReadCaptured()!, schedule.CapturedSlug!, ItemVersionKinds.Scheduled, null, false, schedule, schedule.RedirectOldAddress, cancellationToken)
                            .ConfigureAwait(false)
                        : await ItemPublishing.UnpublishAsync(context, false, schedule, schedule.RedirectToPageId, cancellationToken).ConfigureAwait(false);
                    if (outcome.IsFailure)
                    {
                        return Result.Failure<Item?>(outcome.Error);
                    }

                    schedule.Complete(context.Now);
                    logger.LogInformation(
                        "Scheduled {Action} of item {ItemId} on site {SiteId} ran (schedule {ScheduleId}).",
                        schedule.Action, itemId, due.SiteId, schedule.Id);
                    return Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Marks the schedule failed with the reason, records it, and re-indexes the item (FR-026).</summary>
    private async Task FailAsync(ItemScheduleDue due, Guid collectionId, Guid itemId, Guid owner, string code, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var writes = scope.ServiceProvider.GetRequiredService<ItemWrites>();
        var failed = await writes.ExecuteAsync(
                new ItemWriteRequest(due.SiteId, collectionId, itemId, null, null, owner),
                async context =>
                {
                    var schedule = await context.Database.ForSite<ItemSchedule>(due.SiteId)
                        .SingleOrDefaultAsync(s => s.Id == due.ScheduleId, cancellationToken)
                        .ConfigureAwait(false);
                    if (schedule is null || !schedule.IsDue(due.Revision))
                    {
                        return Result.Success<Item?>(context.Item);
                    }

                    schedule.Fail(code, context.Now);
                    context.Item!.RecordSchedule(ItemHistoryKinds.ScheduleFailed, schedule.Action, schedule.DueAtUtc, code, owner, context.Now);
                    context.Changed(context.Item);
                    return Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (failed.IsFailure)
        {
            throw new InvalidOperationException($"A failed schedule could not be recorded ({failed.Error.Code}); the outbox retries it.");
        }

        logger.LogWarning(
            "Scheduled action of item {ItemId} on site {SiteId} failed with {FailureCode} (schedule {ScheduleId}).",
            itemId, due.SiteId, code, due.ScheduleId);
    }

    private async Task<bool> StillDueAsync(ItemScheduleDue due, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        return await database.ForSite<ItemSchedule>(due.SiteId)
            .AnyAsync(s => s.Id == due.ScheduleId && s.State == ItemScheduleStates.Pending && s.Revision == due.Revision, cancellationToken)
            .ConfigureAwait(false);
    }
}
