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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var item = context.Item!;

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
    public static async Task<Result> UnpublishAsync(ItemWriteContext context, bool cancelSchedule, ItemSchedule? schedule, CancellationToken cancellationToken)
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

        item.Unpublish(schedule?.Action, context.Actor, context.Now);
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
