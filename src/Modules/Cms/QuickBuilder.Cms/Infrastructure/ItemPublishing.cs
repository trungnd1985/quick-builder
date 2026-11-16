using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Publishing an item inside an <see cref="ItemWrites"/> operation: the publish checks, the new
/// version with retention, and the pending schedules a publish or unpublish affects (015 FR-019
/// to FR-023, FR-027; research IT-08).
/// </summary>
/// <remarks>
/// Shared by manual publishes, rollbacks, unpublishes and scheduled runs, so they behave the same.
/// A manual publish, unpublish or rollback cancels a pending scheduled publish, but only when the
/// member confirmed it (<c>cancelSchedule</c>); otherwise it is refused with
/// <c>cms.schedule_pending</c> so the admin can warn first. An unpublish also cancels a pending
/// scheduled unpublish, which would have nothing left to do.
/// </remarks>
internal static class ItemPublishing
{
    /// <summary>
    /// Publishes <paramref name="values"/> and <paramref name="slug"/> as the item's next version,
    /// after checking them against today's rules. <paramref name="schedule"/> is the schedule
    /// being run, when it is one.
    /// </summary>
    public static async Task<Result<ItemVersion>> PublishAsync(
        ItemWriteContext context,
        ItemValues values,
        string slug,
        string kind,
        int? restoredFrom,
        bool cancelSchedule,
        ItemSchedule? schedule,
        CancellationToken cancellationToken) =>
        await PublishAsync(context, values, slug, kind, restoredFrom, cancelSchedule, schedule, redirectOldAddress: false, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// As the other overload; when <paramref name="redirectOldAddress"/> and the published slug
    /// changes, also asks Pages to redirect the old address, in this transaction (017 FR-029).
    /// </summary>
    public static async Task<Result<ItemVersion>> PublishAsync(
        ItemWriteContext context,
        ItemValues values,
        string slug,
        string kind,
        int? restoredFrom,
        bool cancelSchedule,
        ItemSchedule? schedule,
        bool redirectOldAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var item = context.Item!;
        var oldSlug = item.PublishedSlug;

        var report = await context.ValidateAsync(item.Id, slug, values, cancellationToken).ConfigureAwait(false);
        if (!report.IsEmpty)
        {
            var problems = ItemReads.Report(report);
            return Result.Failure<ItemVersion>(kind == ItemVersionKinds.Rollback
                ? CmsErrors.VersionNotPublishable(problems)
                : CmsErrors.ItemNeedsAttention(problems));
        }

        if (schedule is null)
        {
            var cancelled = await CancelPendingAsync(context, ItemScheduleActions.Publish, cancelSchedule, cancellationToken).ConfigureAwait(false);
            if (cancelled.IsFailure)
            {
                return Result.Failure<ItemVersion>(cancelled.Error);
            }
        }

        var version = item.Publish(values, slug, kind, restoredFrom, schedule?.Action, context.Actor, context.Now);
        context.Database.ItemVersions.Add(version);

        if (redirectOldAddress && oldSlug is not null && !string.Equals(oldSlug, slug, StringComparison.Ordinal))
        {
            item.RequestRedirect(oldSlug, Contracts.Events.ItemRedirectReasons.Moved, targetPageId: null, context.Now);
            context.RedirectRequested = true;
        }

        // FR-023: the 50 newest versions are kept; the live one is the newest, so never removed.
        var oldest = version.Number - Item.KeptVersions;
        await context.Database.ForSite<ItemVersion>(context.SiteId)
            .Where(v => v.ItemId == item.Id && v.Number <= oldest)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        context.Changed(item, versionsChanged: true);
        return Result.Success(version);
    }

    /// <summary>Removes the item's published values (FR-021), and the schedules that would act on them.</summary>
    public static Task<Result> UnpublishAsync(ItemWriteContext context, bool cancelSchedule, ItemSchedule? schedule, CancellationToken cancellationToken) =>
        UnpublishAsync(context, cancelSchedule, schedule, redirectToPageId: null, cancellationToken);

    /// <summary>As the other overload; with <paramref name="redirectToPageId"/>, also asks Pages to redirect the address (017 FR-032).</summary>
    public static async Task<Result> UnpublishAsync(
        ItemWriteContext context, bool cancelSchedule, ItemSchedule? schedule, Guid? redirectToPageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var item = context.Item!;

        if (!item.IsPublished)
        {
            return Result.Failure(CmsErrors.ItemNotPublished);
        }

        if (schedule is null)
        {
            var publish = await CancelPendingAsync(context, ItemScheduleActions.Publish, cancelSchedule, cancellationToken).ConfigureAwait(false);
            if (publish.IsFailure)
            {
                return publish;
            }

            await CancelPendingAsync(context, ItemScheduleActions.Unpublish, confirmed: true, cancellationToken).ConfigureAwait(false);
        }

        RequestRemovedRedirect(context, item, redirectToPageId);
        item.Unpublish(schedule?.Action, context.Actor, context.Now);
        context.Changed(item);
        return Result.Success();
    }

    /// <summary>
    /// The redirect accepted when a live item stops being live (017 FR-032): its address to a
    /// page. Asked before the published slug goes.
    /// </summary>
    public static void RequestRemovedRedirect(ItemWriteContext context, Item item, Guid? redirectToPageId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);

        if (redirectToPageId is { } target && item.PublishedSlug is { } slug)
        {
            item.RequestRedirect(slug, Contracts.Events.ItemRedirectReasons.Removed, target, context.Now);
            context.RedirectRequested = true;
        }
    }

    /// <summary>
    /// Moves the item to the trash (021 FR-001, FR-003, research TR-07): a published item is
    /// unpublished as part of it, and its pending schedules are cancelled. Its versions and
    /// history stay. The caller has checked that nothing refers to it.
    /// </summary>
    public static async Task<Result> TrashAsync(ItemWriteContext context, Guid? redirectToPageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var item = context.Item!;

        var hadSchedule = await PendingAsync(context, ItemScheduleActions.Publish, cancellationToken).ConfigureAwait(false) is not null;
        var wasLive = item.IsPublished;
        if (wasLive)
        {
            var unpublished = await UnpublishAsync(context, cancelSchedule: true, schedule: null, redirectToPageId, cancellationToken).ConfigureAwait(false);
            if (unpublished.IsFailure)
            {
                return unpublished;
            }
        }
        else
        {
            await CancelPendingAsync(context, ItemScheduleActions.Publish, confirmed: true, cancellationToken).ConfigureAwait(false);
            await CancelPendingAsync(context, ItemScheduleActions.Unpublish, confirmed: true, cancellationToken).ConfigureAwait(false);
        }

        item.MoveToTrash(wasLive, hadSchedule, context.Actor, context.Now);
        context.Changed(item);
        return Result.Success();
    }

    /// <summary>The item's pending schedule for <paramref name="action"/>, tracked, or null.</summary>
    public static Task<ItemSchedule?> PendingAsync(ItemWriteContext context, string action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var itemId = context.Item!.Id;
        return context.Database.ForSite<ItemSchedule>(context.SiteId)
            .SingleOrDefaultAsync(s => s.ItemId == itemId && s.Action == action && s.State == ItemScheduleStates.Pending, cancellationToken);
    }

    /// <summary>
    /// Cancels the pending schedule for <paramref name="action"/>, recording it; refuses with
    /// <c>cms.schedule_pending</c> when there is one and the member did not confirm.
    /// </summary>
    private static async Task<Result> CancelPendingAsync(ItemWriteContext context, string action, bool confirmed, CancellationToken cancellationToken)
    {
        var pending = await PendingAsync(context, action, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            return Result.Success();
        }

        if (!confirmed)
        {
            return Result.Failure(CmsErrors.SchedulePending(
                new ScheduleResponse(pending.Action, pending.DueAtUtc, new MemberResponse(pending.CreatedBy, string.Empty))));
        }

        pending.Cancel(context.Now);
        context.Item!.RecordSchedule(ItemHistoryKinds.ScheduleCancelled, action, pending.DueAtUtc, null, context.Actor, context.Now);
        return Result.Success();
    }
}
