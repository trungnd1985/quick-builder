using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Publishing a page or component, taking it down, and rolling it back (012 FR-001 to FR-014,
/// research PR-02 to PR-05, PR-15; data-model §3.1).
/// </summary>
/// <remarks>
/// Each operation is one transaction, so a publish either lands completely — version, live
/// pointer, dependency rows, generations, log entry, integration event — or not at all
/// (FR-001). Two publishes of one subject at once serialise on the publication's <c>xmin</c>:
/// the loser runs again against what the winner committed, so both succeed with consecutive
/// numbers (PR-03). Scheduled runs call <see cref="PublishContentAsync"/> and
/// <see cref="UnpublishLiveAsync"/> inside the transaction <see cref="ScheduleWrites"/> holds.
/// </remarks>
internal sealed class PublishingWrites(
    PagesDbContext database,
    IPageDocumentValidator validator,
    PagesAccess access,
    PublicationReads reads,
    GenerationBump bump,
    ICollectionSchemaReader collections,
    CollectionUseRows uses,
    PageParamRules pageParams,
    RedirectOffers redirectOffers,
    TimeProvider clock,
    ILogger<PublishingWrites> logger)
{
    // Every racer but one loses each round, so n concurrent publishes of one subject need up to
    // n attempts; eight covers far more than people publish at once (PR-03). A short random
    // pause between attempts keeps the racers from colliding again in lockstep.
    private const int Attempts = 8;

    /// <summary>The page or component a publishing action concerns.</summary>
    /// <param name="Kind">One of <see cref="SubjectKinds"/>.</param>
    /// <param name="Id">Its id.</param>
    /// <param name="SiteId">Its site.</param>
    /// <param name="Label">The page title or component name now, for the log.</param>
    /// <param name="Path">The page's address now; null for components.</param>
    /// <param name="TemplateCollectionId">The collection a template page shows (016).</param>
    internal sealed record Subject(string Kind, Guid Id, Guid SiteId, string Label, string? Path, Guid? TemplateCollectionId = null)
    {
        public bool IsPage => Kind == SubjectKinds.Page;
    }

    // --- Manual actions ---------------------------------------------------------------------

    /// <summary>Publishes the draft the caller saw (FR-001, FR-002; PR-03).</summary>
    public Task<Result<PublicationResponse>> PublishAsync(
        Guid siteId, string kind, Guid subjectId, uint draftVersion, CancellationToken cancellationToken) =>
        RunAsync(siteId, kind, subjectId, async (subject, now) =>
        {
            var draft = await DraftAsync(siteId, subject, cancellationToken).ConfigureAwait(false);
            if (draft.Version != draftVersion)
            {
                return Result.Failure(PagesErrors.OutOfDate);
            }

            return await PublishContentAsync(
                    subject,
                    draft,
                    subject.IsPage ? subject.Label : null,
                    draft.Version,
                    VersionKinds.Publish,
                    restoredFromNumber: null,
                    access.Actor,
                    scheduleOwner: null,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Takes a page or component off the public site (FR-004, FR-005).</summary>
    public Task<Result<PublicationResponse>> UnpublishAsync(Guid siteId, string kind, Guid subjectId, CancellationToken cancellationToken) =>
        UnpublishAsync(siteId, kind, subjectId, redirectToPageId: null, cancellationToken);

    /// <summary>
    /// Unpublishes, and when <paramref name="redirectToPageId"/> is given redirects the page's
    /// address to that page in the same transaction (017 FR-032).
    /// </summary>
    public Task<Result<PublicationResponse>> UnpublishAsync(
        Guid siteId, string kind, Guid subjectId, Guid? redirectToPageId, CancellationToken cancellationToken) =>
        RunAsync(siteId, kind, subjectId, async (subject, now) =>
        {
            var unpublished = await UnpublishLiveAsync(subject, access.Actor, scheduleOwner: null, now, cancellationToken).ConfigureAwait(false);
            return unpublished.IsFailure || redirectToPageId is not { } target || !subject.IsPage
                ? unpublished
                : await RedirectRemovedPageAsync(subject, target, access.Actor, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>The redirect accepted when a live page is taken down, saved in the caller's transaction.</summary>
    internal async Task<Result> RedirectRemovedPageAsync(Subject subject, Guid targetPageId, Guid actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var redirected = await redirectOffers.RedirectRemovedPageAsync(subject.SiteId, subject.Id, targetPageId, actor, cancellationToken).ConfigureAwait(false);
        if (redirected.IsFailure)
        {
            return Result.Failure(redirected.Error);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Publishes a retained version again as a new one; the draft is untouched (FR-014).</summary>
    public Task<Result<PublicationResponse>> RollbackAsync(
        Guid siteId, string kind, Guid subjectId, int versionNumber, CancellationToken cancellationToken) =>
        RunAsync(siteId, kind, subjectId, async (subject, now) =>
        {
            var source = await database.ForSite<PublishedVersion>(siteId)
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    v => v.SubjectKind == subject.Kind && v.SubjectId == subject.Id && v.Number == versionNumber,
                    cancellationToken)
                .ConfigureAwait(false);

            if (source is null)
            {
                return Result.Failure(PagesErrors.VersionNotFound);
            }

            var referenced = DependencyExtractor.Components(source.Document);
            if (referenced.Count > 0)
            {
                var existing = await database.ForSite<Component>(siteId)
                    .Where(c => referenced.Contains(c.Id))
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var missing = referenced.Except(existing).ToList();
                if (missing.Count > 0)
                {
                    return Result.Failure(PagesErrors.VersionMissingComponents(missing));
                }
            }

            return await PublishContentAsync(
                    subject,
                    new DraftContent(source.Document, source.SchemaVersion, Version: 0),
                    source.Title,
                    fromDraftVersion: null,
                    VersionKinds.Rollback,
                    source.Number,
                    access.Actor,
                    scheduleOwner: null,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

    // --- Shared steps, also used by scheduled runs -------------------------------------------

    /// <summary>The document a publish starts from.</summary>
    internal sealed record DraftContent(string Document, int SchemaVersion, long Version);

    /// <summary>
    /// Makes <paramref name="content"/> live as the subject's next version. Runs inside the
    /// caller's transaction; saves, but does not commit.
    /// </summary>
    /// <param name="scheduleOwner">For a scheduled publish, who set the schedule; null for a manual action.</param>
    internal async Task<Result> PublishContentAsync(
        Subject subject,
        DraftContent content,
        string? title,
        long? fromDraftVersion,
        string versionKind,
        int? restoredFromNumber,
        Guid actor,
        Guid? scheduleOwner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(content);

        var siteId = subject.SiteId;

        // The document was valid when saved, but the contract may have moved since; nothing
        // invalid is ever made live (FR-001).
        var checkedDocument = validator.Validate(
            Encoding.UTF8.GetBytes(content.Document),
            subject.IsPage ? DocumentKind.Page : DocumentKind.Component);
        if (!checkedDocument.IsValid)
        {
            return Result.Failure(PagesErrors.InvalidDocument(checkedDocument));
        }

        // 035 FR-014: a rich text image needs alternative text, or to be decorative, to go live.
        using (var parsed = JsonDocument.Parse(content.Document))
        {
            var withoutAlt = RichTextRules.RichTextBlocks(parsed.RootElement)
                .SelectMany(block => RichTextRules.ImagesWithoutAlt(block.Content, block.Location))
                .ToList();
            if (withoutAlt.Count > 0)
            {
                return Result.Failure(PagesErrors.ImageAltMissing(withoutAlt));
            }
        }

        // FR-003: a page may show only components visitors can already get.
        var components = DependencyExtractor.Components(content.Document);
        if (components.Count > 0)
        {
            var live = await database.ForSite<Publication>(siteId)
                .Where(p => p.SubjectKind == SubjectKinds.Component && components.Contains(p.SubjectId) && p.LiveVersionId != null)
                .Select(p => p.SubjectId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var unpublished = components.Except(live).ToList();
            if (unpublished.Count > 0)
            {
                return Result.Failure(PagesErrors.UnpublishedComponents(unpublished));
            }
        }

        // Rule 21 (016 FR-027): visitors never get two lists that turn pages together.
        var pageParamTaken = subject.IsPage
            ? await pageParams.CheckPageAsync(siteId, content.Document, live: true, cancellationToken).ConfigureAwait(false)
            : await pageParams.CheckComponentAsync(siteId, subject.Id, content.Document, live: true, cancellationToken).ConfigureAwait(false);
        if (pageParamTaken is not null)
        {
            return Result.Failure(pageParamTaken);
        }

        var publication = await PublicationAsync(siteId, subject, create: true, cancellationToken).ConfigureAwait(false);
        var version = publication!.Publish(
            content.Document, content.SchemaVersion, title, fromDraftVersion, versionKind, restoredFromNumber, actor, subject.Path, now);
        publication.SetPageParamNames(DependencyExtractor.PageParamNames(content.Document));
        database.PublishedVersions.Add(version);

        // 013 research MV-05: which files this version shows, for usage (live now, history
        // once another version replaces it). Removed with the version, by cascade.
        database.VersionMediaReferences.AddRange(DependencyExtractor.Media(content.Document)
            .Select(mediaId => new VersionMediaReference(siteId, version.Id, mediaId)));

        await ReplaceDependenciesAsync(siteId, subject, content.Document, cancellationToken).ConfigureAwait(false);

        // 016 TB-13: the collections the live version uses.
        await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Live, content.Document, subject.TemplateCollectionId, cancellationToken)
            .ConfigureAwait(false);

        if (scheduleOwner is null)
        {
            // FR-019: a manual publish or rollback supersedes a pending scheduled publish.
            await CancelSchedulesAsync(siteId, subject, [ScheduleActions.Publish], actor, now, cancellationToken).ConfigureAwait(false);
        }

        Log(siteId, subject, LogActionFor(versionKind), now, actor, scheduleOwner, version.Number, restoredFromNumber);

        PagesOutbox.AddFrom(database, publication, scheduleOwner is null ? actor : null);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AfterLiveChangeAsync(siteId, subject, publication, cancellationToken).ConfigureAwait(false);

        // Ids and numbers only: a title or a path can itself be content (FR-044).
        logger.LogInformation(
            "{SubjectKind} {SubjectId} of site {SiteId} is live as version {VersionNumber} ({VersionKind}).",
            subject.Kind, subject.Id, siteId, version.Number, versionKind);
        return Result.Success();
    }

    /// <summary>
    /// Takes the subject off the public site. Runs inside the caller's transaction; saves, but
    /// does not commit.
    /// </summary>
    internal async Task<Result> UnpublishLiveAsync(
        Subject subject, Guid actor, Guid? scheduleOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var siteId = subject.SiteId;
        var publication = await PublicationAsync(siteId, subject, create: false, cancellationToken).ConfigureAwait(false);

        if (publication is not { IsLive: true })
        {
            return Result.Failure(PagesErrors.NotPublished);
        }

        if (!subject.IsPage)
        {
            // FR-004: never take away a component that published pages still show.
            var users = await LivePagesUsingAsync(siteId, subject.Id, cancellationToken).ConfigureAwait(false);
            if (users > 0)
            {
                return Result.Failure(PagesErrors.ComponentInUseByPublishedPages(users));
            }
        }

        publication.Unpublish(scheduled: scheduleOwner is not null, subject.Path, now);
        await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Live, null, null, cancellationToken).ConfigureAwait(false);

        await database.ForSite<LiveDependency>(siteId)
            .Where(d => d.SubjectKind == subject.Kind && d.SubjectId == subject.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (scheduleOwner is null)
        {
            // A manual unpublish supersedes a pending scheduled publish (FR-019), and leaves a
            // pending scheduled unpublish nothing to do.
            await CancelSchedulesAsync(
                    siteId, subject, [ScheduleActions.Publish, ScheduleActions.Unpublish], actor, now, cancellationToken)
                .ConfigureAwait(false);
        }

        Log(siteId, subject, scheduleOwner is null ? LogActions.Unpublish : LogActions.ScheduledUnpublish, now, actor, scheduleOwner);

        PagesOutbox.AddFrom(database, publication, scheduleOwner is null ? actor : null);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AfterLiveChangeAsync(siteId, subject, publication, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "{SubjectKind} {SubjectId} of site {SiteId} was unpublished (scheduled: {Scheduled}).",
            subject.Kind, subject.Id, siteId, scheduleOwner is not null);
        return Result.Success();
    }

    /// <summary>
    /// Removes a subject's publishing records before the subject itself is deleted: its
    /// publication (and with it every version), dependencies and schedules. The log keeps its
    /// entries, and gains a <c>deleted</c> one when there were any (FR-006, FR-017, PR-15). Runs
    /// in the caller's transaction; the caller saves.
    /// </summary>
    internal async Task ForgetAsync(Subject subject, Guid actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var siteId = subject.SiteId;

        await uses.ForgetAsync(siteId, subject.Kind, subject.Id, cancellationToken).ConfigureAwait(false);

        await database.ForSite<LiveDependency>(siteId)
            .Where(d => d.SubjectKind == subject.Kind && d.SubjectId == subject.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await database.ForSite<PublishSchedule>(siteId)
            .Where(s => s.SubjectKind == subject.Kind && s.SubjectId == subject.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // 013 research MV-05: the draft's media usage goes with the subject. (Its versions'
        // references go with the versions.)
        await database.ForSite<DraftMediaReference>(siteId)
            .Where(r => r.SubjectKind == subject.Kind && r.SubjectId == subject.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Versions go with their publication (foreign key, cascade).
        await database.ForSite<Publication>(siteId)
            .Where(p => p.SubjectKind == subject.Kind && p.SubjectId == subject.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasHistory = await database.ForSite<PublishLogEntry>(siteId)
            .AnyAsync(e => e.SubjectKind == subject.Kind && e.SubjectId == subject.Id, cancellationToken)
            .ConfigureAwait(false);

        if (hasHistory)
        {
            Log(siteId, subject, LogActions.Deleted, clock.GetUtcNow(), actor);
        }
    }

    /// <summary>How many live pages show the component.</summary>
    internal Task<int> LivePagesUsingAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken) =>
        database.ForSite<LiveDependency>(siteId)
            .Where(d => d.SubjectKind == SubjectKinds.Page && d.TargetKind == DependencyKinds.Component && d.TargetId == componentId)
            .Select(d => d.SubjectId)
            .Distinct()
            .CountAsync(cancellationToken);

    /// <summary>The page or component, as a publishing action sees it; null when the site has none.</summary>
    internal async Task<Subject?> SubjectAsync(Guid siteId, string kind, Guid subjectId, CancellationToken cancellationToken)
    {
        if (kind == SubjectKinds.Page)
        {
            var page = await database.ForSite<Page>(siteId)
                .AsNoTracking()
                .Where(p => p.Id == subjectId)
                .Select(p => new { p.Title, p.Path, p.TemplateCollectionId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return page is null ? null : new Subject(kind, subjectId, siteId, page.Title, page.Path, page.TemplateCollectionId);
        }

        var name = await database.ForSite<Component>(siteId)
            .AsNoTracking()
            .Where(c => c.Id == subjectId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return name is null ? null : new Subject(kind, subjectId, siteId, name, Path: null);
    }

    /// <summary>The subject's draft document and version.</summary>
    internal async Task<DraftContent> DraftAsync(Guid siteId, Subject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // xmin is read as it is and widened after: PostgreSQL has no cast from xid to bigint.
        if (subject.IsPage)
        {
            var page = await database.ForSite<PageDraft>(siteId)
                .AsNoTracking()
                .Where(d => d.PageId == subject.Id)
                .Select(d => new { d.Document, d.SchemaVersion, d.Version })
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);

            return new DraftContent(page.Document, page.SchemaVersion, page.Version);
        }

        var component = await database.ForSite<ComponentDraft>(siteId)
            .AsNoTracking()
            .Where(d => d.ComponentId == subject.Id)
            .Select(d => new { d.Document, d.SchemaVersion, d.Version })
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DraftContent(component.Document, component.SchemaVersion, component.Version);
    }

    /// <summary>Appends a publish log entry, to be saved with the change (FR-012).</summary>
    internal void Log(
        Guid siteId,
        Subject subject,
        string action,
        DateTimeOffset now,
        Guid actor,
        Guid? scheduleOwner = null,
        int? versionNumber = null,
        int? restoredFromNumber = null,
        string? reasonCode = null)
    {
        ArgumentNullException.ThrowIfNull(subject);

        database.PublishLog.Add(new PublishLogEntry(
            Guid.CreateVersion7(), siteId, subject.Kind, subject.Id, subject.Label, subject.Path, action, now)
        {
            VersionNumber = versionNumber,
            RestoredFromNumber = restoredFromNumber,
            ActorUserId = scheduleOwner is null ? actor : null,
            ScheduleOwnerId = scheduleOwner,
            ReasonCode = reasonCode,
        });
    }

    /// <summary>
    /// Cancels the subject's pending schedules for <paramref name="actions"/> and clears any
    /// failed one, because a person has now acted on the subject themselves (FR-019).
    /// </summary>
    internal async Task CancelSchedulesAsync(
        Guid siteId, Subject subject, string[] actions, Guid actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await database.ForSite<PublishSchedule>(siteId)
            .Where(s => s.SubjectKind == subject.Kind && s.SubjectId == subject.Id
                        && s.State == ScheduleStates.Pending && actions.Contains(s.Action))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var schedule in pending)
        {
            schedule.Cancel(now);
            Log(siteId, subject, LogActions.ScheduleCancelled, now, actor, reasonCode: LogReasons.SupersededByManualAction);
        }

        // 016 TB-13: a cancelled scheduled publish no longer uses anything.
        if (pending.Exists(s => s.Action == ScheduleActions.Publish))
        {
            await uses.StageAsync(siteId, subject.Kind, subject.Id, CollectionUsageStates.Scheduled, null, null, cancellationToken).ConfigureAwait(false);
        }

        // A failure is shown until someone acts on the subject; the log keeps it.
        await database.ForSite<PublishSchedule>(siteId)
            .Where(s => s.SubjectKind == subject.Kind && s.SubjectId == subject.Id && s.State == ScheduleStates.Failed)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> in its own transaction, again on a lost race, and
    /// returns the subject's publication afterwards.
    /// </summary>
    private async Task<Result<PublicationResponse>> RunAsync(
        Guid siteId,
        string kind,
        Guid subjectId,
        Func<Subject, DateTimeOffset, Task<Result>> attempt,
        CancellationToken cancellationToken)
    {
        if (!await access.SiteExistsAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PublicationResponse>(PagesErrors.SiteNotFound);
        }

        for (var i = 1; ; i++)
        {
            var subject = await SubjectAsync(siteId, kind, subjectId, cancellationToken).ConfigureAwait(false);
            if (subject is null)
            {
                return Result.Failure<PublicationResponse>(kind == SubjectKinds.Page ? PagesErrors.NotFound : PagesErrors.ComponentNotFound);
            }

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await attempt(subject, clock.GetUtcNow()).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    database.ChangeTracker.Clear();
                    return Result.Failure<PublicationResponse>(result.Error);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsLostRace(ex))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();

                if (i >= Attempts)
                {
                    return Result.Failure<PublicationResponse>(PagesErrors.OutOfDate);
                }

#pragma warning disable CA5394 // Jitter, not security.
                await Task.Delay(Random.Shared.Next(5, 25 * i), cancellationToken).ConfigureAwait(false);
#pragma warning restore CA5394
                continue;
            }

            database.ChangeTracker.Clear();
            return Result.Success(await reads.SummaryAsync(siteId, kind, subjectId, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Another publish of the same subject committed first: its publication's version moved, or
    /// its first publication or a version number was taken between our read and our write.
    /// </summary>
    internal static bool IsLostRace(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_publications" or "ux_published_versions_subject_number",
        };

    private async Task<Publication?> PublicationAsync(Guid siteId, Subject subject, bool create, CancellationToken cancellationToken)
    {
        var publication = await database.ForSite<Publication>(siteId)
            .FirstOrDefaultAsync(p => p.SubjectKind == subject.Kind && p.SubjectId == subject.Id, cancellationToken)
            .ConfigureAwait(false);

        if (publication is null && create)
        {
            publication = Publication.Start(siteId, subject.Kind, subject.Id);
            database.Publications.Add(publication);
        }

        return publication;
    }

    private async Task ReplaceDependenciesAsync(Guid siteId, Subject subject, string document, CancellationToken cancellationToken)
    {
        await database.ForSite<LiveDependency>(siteId)
            .Where(d => d.SubjectKind == subject.Kind && d.SubjectId == subject.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var targets = DependencyExtractor.Extract(document).ToHashSet();

        // 016 TB-10: a template depends on its collection, and a list over a multi-reference
        // field on the field's target collection, known only from the schemas.
        if (subject.TemplateCollectionId is { } templateCollection)
        {
            targets.Add((DependencyKinds.Template, templateCollection));
        }

        var fields = DependencyExtractor.ListSourceFields(document);
        if (fields.Count > 0)
        {
            var owners = targets.Where(t => t.Kind is DependencyKinds.Collection or DependencyKinds.Template).Select(t => t.Id).Distinct().ToList();
            var schemas = await collections.GetSchemasAsync(siteId, owners, cancellationToken).ConfigureAwait(false);
            foreach (var target in MultiReferenceTargets(schemas.Values, fields))
            {
                targets.Add((DependencyKinds.Collection, target));
            }
        }

        database.LiveDependencies.AddRange(targets
            .Select(t => new LiveDependency(siteId, subject.Kind, subject.Id, t.Kind, t.Id)));
    }

    /// <summary>The target collections of the multi-reference fields named <paramref name="fieldIds"/>.</summary>
    private static IEnumerable<Guid> MultiReferenceTargets(IEnumerable<JsonElement> schemas, IReadOnlySet<string> fieldIds)
    {
        foreach (var schema in schemas)
        {
            if (schema.ValueKind != JsonValueKind.Object
                || !schema.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind == JsonValueKind.Object
                    && field.TryGetProperty("id", out var id) && id.GetString() is { } fieldId && fieldIds.Contains(fieldId)
                    && field.TryGetProperty("type", out var type) && type.GetString() == "multiReference"
                    && field.TryGetProperty("target", out var target) && Guid.TryParse(target.GetString(), out var targetId))
                {
                    yield return targetId;
                }
            }
        }
    }

    /// <summary>
    /// After the subject's own rows are saved: the pages that show it refresh (PR-05), and
    /// versions beyond the retained ones go (PR-15). Both are set-based statements in the same
    /// transaction.
    /// </summary>
    private async Task AfterLiveChangeAsync(Guid siteId, Subject subject, Publication publication, CancellationToken cancellationToken)
    {
        var target = subject.IsPage ? (DependencyKinds.Page, subject.Id) : (DependencyKinds.Component, subject.Id);
        await bump.BumpForTargetsAsync(siteId, [target], exceptPageId: subject.IsPage ? subject.Id : null, cancellationToken)
            .ConfigureAwait(false);

        // 017 research SE-15: a page or template going live, changing or leaving changes the
        // sitemap. Its own rows are saved already, so a new generation row needs a save too.
        if (subject.IsPage && await bump.Sitemap.BumpAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // 016 TB-10 row 3: the item links of every list of the collection change with its template.
        if (subject.TemplateCollectionId is { } templateCollection)
        {
            await bump.BumpForCollectionsAsync(siteId, [templateCollection], includeTemplates: true, exceptPageId: subject.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        if (publication.LiveNumber is { } live && live > Publication.RetainedVersions)
        {
            var oldest = live - Publication.RetainedVersions;
            var liveId = publication.LiveVersionId;

            await database.ForSite<PublishedVersion>(siteId)
                .Where(v => v.SubjectKind == subject.Kind && v.SubjectId == subject.Id && v.Number <= oldest && v.Id != liveId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string LogActionFor(string versionKind) => versionKind switch
    {
        VersionKinds.Scheduled => LogActions.ScheduledPublish,
        VersionKinds.Rollback => LogActions.Rollback,
        _ => LogActions.Publish,
    };
}
