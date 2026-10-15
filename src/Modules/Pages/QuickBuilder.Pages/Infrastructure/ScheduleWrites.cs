using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features;
using QuickBuilder.Pages.Features.RunDueSchedule;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Setting, changing, cancelling and running scheduled publishes and unpublishes (012 FR-018
/// to FR-021, research PR-14; data-model §1.4, §4.2).
/// </summary>
/// <remarks>
/// Setting a schedule writes the schedule and its due message in one transaction; the
/// message waits in the outbox until its time. Running it is the same domain operation as a
/// manual publish or unpublish, in one transaction with the schedule's state change, which
/// is also what makes it run once: a redelivery, or the message of an older revision, finds
/// the schedule no longer due.
/// </remarks>
internal sealed class ScheduleWrites(
    PagesDbContext database,
    PagesAccess access,
    PublishingWrites publishing,
    CollectionUseRows uses,
    TimeProvider clock,
    ILogger<ScheduleWrites> logger)
{
    // A lost race is another member setting a schedule for the same subject and action at the
    // same moment; the next attempt finds theirs and changes it.
    private const int Attempts = 3;

    /// <summary>Sets the pending schedule for <paramref name="action"/>, or moves the one there is.</summary>
    public Task<Result<ScheduleResponse>> SetAsync(
        Guid siteId, string kind, Guid subjectId, string action, DateTimeOffset dueAtUtc, uint? draftVersion, CancellationToken cancellationToken) =>
        SetAsync(siteId, kind, subjectId, action, dueAtUtc, draftVersion, redirectToPageId: null, cancellationToken);

    /// <summary>
    /// As the other overload; a page unpublish also keeps where its address will redirect
    /// (017 FR-032, tasks T118).
    /// </summary>
    public async Task<Result<ScheduleResponse>> SetAsync(
        Guid siteId, string kind, Guid subjectId, string action, DateTimeOffset dueAtUtc, uint? draftVersion, Guid? redirectToPageId, CancellationToken cancellationToken)
    {
        if (!await access.SiteExistsAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ScheduleResponse>(PagesErrors.SiteNotFound);
        }

        for (var i = 1; ; i++)
        {
            var subject = await publishing.SubjectAsync(siteId, kind, subjectId, cancellationToken).ConfigureAwait(false);
            if (subject is null)
            {
                return Result.Failure<ScheduleResponse>(PublishingWrites.NotFoundFor(kind));
            }

            var now = clock.GetUtcNow();
            if (dueAtUtc <= now)
            {
                return Result.Failure<ScheduleResponse>(PagesErrors.ScheduleInPast);
            }

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await SetInTransactionAsync(subject, action, dueAtUtc, draftVersion, redirectToPageId, now, cancellationToken).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    database.ChangeTracker.Clear();
                    return Result.Failure<ScheduleResponse>(result.Error);
                }

                logger.LogInformation(
                    "A scheduled {Action} of {SubjectKind} {SubjectId} on site {SiteId} is due at {DueAtUtc} (revision {Revision}).",
                    action, kind, subjectId, siteId, dueAtUtc, result.Value.Revision);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();

                var names = await access.NamesAsync([result.Value.CreatedBy], cancellationToken).ConfigureAwait(false);
                return Result.Success(PublicationReads.Schedule(result.Value, names));
            }
            catch (DbUpdateException ex) when (IsLostRace(ex) && i < Attempts)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Cancels the pending schedule for <paramref name="action"/>.</summary>
    public async Task<Result> CancelAsync(Guid siteId, string kind, Guid subjectId, string action, CancellationToken cancellationToken)
    {
        if (!await access.SiteExistsAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(PagesErrors.SiteNotFound);
        }

        var subject = await publishing.SubjectAsync(siteId, kind, subjectId, cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            return Result.Failure(PublishingWrites.NotFoundFor(kind));
        }

        var now = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Guarded like a run, so a schedule that has just run is not reported cancelled.
        var cancelled = await database.ForSite<PublishSchedule>(siteId)
            .Where(s => s.SubjectKind == subject.Kind && s.SubjectId == subject.Id && s.Action == action && s.State == ScheduleStates.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.State, ScheduleStates.Cancelled).SetProperty(x => x.CompletedAtUtc, now),
                cancellationToken)
            .ConfigureAwait(false);
        if (cancelled == 0)
        {
            return Result.Failure(PagesErrors.ScheduleNotFound);
        }

        publishing.Log(siteId, subject, LogActions.ScheduleCancelled, now, access.Actor);
        if (action == ScheduleActions.Publish)
        {
            // 016 TB-13: content that will no longer go live uses nothing.
            await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Scheduled, null, null, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>
    /// Runs a due schedule (PR-14). Does nothing unless the schedule is still pending at
    /// <paramref name="revision"/>. A domain refusal marks it failed and is logged; it is not
    /// an error of the message, so the outbox does not retry it.
    /// </summary>
    public async Task RunAsync(Guid siteId, Guid scheduleId, int revision, CancellationToken cancellationToken)
    {
        for (var i = 1; ; i++)
        {
            var schedule = await database.ForSite<PublishSchedule>(siteId)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken)
                .ConfigureAwait(false);

            if (schedule is null || !schedule.IsDue(revision))
            {
                return;
            }

            // The schedule goes with its subject, so a subject that is gone has nothing due.
            var subject = await publishing.SubjectAsync(siteId, schedule.SubjectKind, schedule.SubjectId, cancellationToken).ConfigureAwait(false);
            if (subject is null)
            {
                return;
            }

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var now = clock.GetUtcNow();

                // Claimed first, by a guarded update: the row lock serialises this run with a
                // cancel or a second run, and no row means one of them got there first. A
                // refusal below rolls the claim back with everything else.
                var claimed = await database.ForSite<PublishSchedule>(siteId)
                    .Where(s => s.Id == scheduleId && s.State == ScheduleStates.Pending && s.Revision == revision)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.State, ScheduleStates.Done).SetProperty(x => x.CompletedAtUtc, now),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (claimed == 0)
                {
                    return;
                }

                var owner = schedule.CreatedBy;
                var result = schedule.Action == ScheduleActions.Publish
                    ? await publishing.PublishContentAsync(
                            subject,
                            new PublishingWrites.DraftContent(schedule.CapturedDocument!, schedule.CapturedSchemaVersion!.Value, schedule.CapturedDraftVersion ?? 0),
                            subject.IsPage ? schedule.CapturedTitle : subject.VersionTitle,
                            schedule.CapturedDraftVersion,
                            VersionKinds.Scheduled,
                            restoredFromNumber: null,
                            owner,
                            scheduleOwner: owner,
                            now,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await publishing.UnpublishLiveAsync(subject, owner, scheduleOwner: owner, now, cancellationToken).ConfigureAwait(false);

                // 017 FR-032: the accepted redirect, in the unpublish's transaction. A target page
                // deleted since the schedule was set leaves the address without one, not the page live.
                if (result.IsSuccess && schedule.Action == ScheduleActions.Unpublish && schedule.RedirectToPageId is { } redirectTo)
                {
                    await publishing.RedirectRemovedPageAsync(subject, redirectTo, owner, cancellationToken).ConfigureAwait(false);
                }

                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    database.ChangeTracker.Clear();
                    await FailAsync(siteId, subject, scheduleId, revision, result.Error.Code, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (schedule.Action == ScheduleActions.Publish)
                {
                    // 016 TB-13: what was scheduled is live now, and no longer pending.
                    await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Scheduled, null, null, cancellationToken).ConfigureAwait(false);
                    await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateException ex) when ((ex is DbUpdateConcurrencyException || PublishingWrites.IsLostRace(ex)) && i < Attempts)
            {
                // A manual action on the subject committed first; the next attempt sees it.
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
            }
        }
    }

    private async Task<Result<PublishSchedule>> SetInTransactionAsync(
        PublishingWrites.Subject subject, string action, DateTimeOffset dueAtUtc, uint? draftVersion,
        Guid? redirectToPageId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var siteId = subject.SiteId;
        PublishingWrites.DraftContent? draft = null;

        if (action == ScheduleActions.Publish)
        {
            if (draftVersion is null)
            {
                return Result.Failure<PublishSchedule>(PagesErrors.DraftVersionRequired);
            }

            // FR-019: what goes live is the draft the member reviewed, checked now.
            draft = await publishing.DraftAsync(siteId, subject, cancellationToken).ConfigureAwait(false);
            if (draft.Version != draftVersion)
            {
                return Result.Failure<PublishSchedule>(PagesErrors.OutOfDate);
            }

            var checkedDocument = publishing.Validate(subject, draft.Document);
            if (!checkedDocument.IsValid)
            {
                return Result.Failure<PublishSchedule>(PagesErrors.InvalidDocument(checkedDocument));
            }
        }
        else
        {
            var live = await database.ForSite<Publication>(siteId)
                .AnyAsync(p => p.SubjectKind == subject.Kind && p.SubjectId == subject.Id && p.LiveVersionId != null, cancellationToken)
                .ConfigureAwait(false);
            if (!live)
            {
                return Result.Failure<PublishSchedule>(PagesErrors.NotPublished);
            }
        }

        var actor = access.Actor;
        var title = subject.VersionTitle;
        var schedule = await PendingAsync(subject, action, cancellationToken).ConfigureAwait(false);

        if (schedule is null)
        {
            schedule = action == ScheduleActions.Publish
                ? PublishSchedule.ForPublish(siteId, subject.Kind, subject.Id, dueAtUtc, draft!.Document, draft.SchemaVersion, title, draft.Version, actor, now)
                : PublishSchedule.ForUnpublish(siteId, subject.Id, dueAtUtc, actor, now);
            database.PublishSchedules.Add(schedule);
            publishing.Log(siteId, subject, LogActions.ScheduleSet, now, actor);
        }
        else
        {
            schedule.Reschedule(dueAtUtc, actor);
            if (draft is not null)
            {
                schedule.Recapture(draft.Document, draft.SchemaVersion, title, draft.Version);
            }

            publishing.Log(siteId, subject, LogActions.ScheduleChanged, now, actor);
        }

        if (action == ScheduleActions.Unpublish)
        {
            if (redirectToPageId is { } target
                && !await database.ForSite<Page>(siteId).AnyAsync(p => p.Id == target && p.Id != subject.Id && p.TemplateCollectionId == null, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<PublishSchedule>(PagesErrors.RedirectTargetInvalid("choose another page of this site"));
            }

            schedule.RedirectTo(redirectToPageId);
        }

        // 016 TB-13: the collections the captured content uses, pending with it.
        if (draft is not null)
        {
            await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Scheduled, draft.Document, subject.TemplateCollectionId, cancellationToken)
                .ConfigureAwait(false);
        }

        // A failed schedule is shown until someone acts on the subject; setting one is acting.
        await database.ForSite<PublishSchedule>(siteId)
            .Where(s => s.SubjectKind == subject.Kind && s.SubjectId == subject.Id && s.State == ScheduleStates.Failed)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        PagesOutbox.Add(
            database,
            new PublishScheduleDue(Guid.CreateVersion7(), now, siteId, schedule.Id, schedule.Revision),
            availableAtUtc: dueAtUtc);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(schedule);
    }

    /// <summary>Marks the schedule failed with the refusal's code, and logs it.</summary>
    private async Task FailAsync(
        Guid siteId, PublishingWrites.Subject subject, Guid scheduleId, int revision, string code, CancellationToken cancellationToken)
    {
        var schedule = await database.ForSite<PublishSchedule>(siteId)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken)
            .ConfigureAwait(false);
        if (schedule is null || !schedule.IsDue(revision))
        {
            return;
        }

        var now = clock.GetUtcNow();
        schedule.Fail(code, now);
        logger.LogWarning(
            "Schedule {ScheduleId} of {SubjectKind} {SubjectId} on site {SiteId} failed: {FailureCode}.",
            scheduleId, subject.Kind, subject.Id, siteId, code);
        publishing.Log(siteId, subject, LogActions.ScheduleFailed, now, schedule.CreatedBy, scheduleOwner: schedule.CreatedBy, reasonCode: code);
        if (schedule.Action == ScheduleActions.Publish)
        {
            // 016 TB-13: content that failed to go live is no longer pending.
            await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Scheduled, null, null, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
    }

    private Task<PublishSchedule?> PendingAsync(PublishingWrites.Subject subject, string action, CancellationToken cancellationToken) =>
        database.ForSite<PublishSchedule>(subject.SiteId)
            .FirstOrDefaultAsync(
                s => s.SubjectKind == subject.Kind && s.SubjectId == subject.Id && s.Action == action && s.State == ScheduleStates.Pending,
                cancellationToken);

    private static bool IsLostRace(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_publish_schedules_pending",
        };
}
