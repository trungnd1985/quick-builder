using Microsoft.EntityFrameworkCore;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// The Forms module's data context. Owns the <c>forms</c> schema and nothing else (018
/// data-model §1).
/// </summary>
/// <remarks>
/// Every table is site-owned and filtered against <see cref="CurrentSiteId"/>. Admin endpoints
/// name their site in the route and read through <see cref="ForSite{T}"/>, as Pages and Cms do.
/// The public submit endpoint runs on a resolved site, where the filter applies as it is. The
/// notification worker and the sweep run on no site; their cross-site reads are the methods
/// below, each with its reason, and nowhere else.
/// </remarks>
internal sealed class FormsDbContext(DbContextOptions<FormsDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "forms";

    internal DbSet<FormRecord> Forms => Set<FormRecord>();

    internal DbSet<FormPlacement> FormPlacements => Set<FormPlacement>();

    internal DbSet<Submission> Submissions => Set<Submission>();

    internal DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    internal DbSet<SpamCounter> SpamCounters => Set<SpamCounter>();

    internal DbSet<RateWindow> RateWindows => Set<RateWindow>();

    internal DbSet<SubmissionLogEntry> SubmissionLog => Set<SubmissionLogEntry>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>
    /// The site the query filter compares against, referenced through <c>this</c> for the
    /// reason <c>SiteQueryFilterGuidance</c> documents. <see cref="Guid.Empty"/> when no site is
    /// resolved, which fails closed.
    /// </summary>
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    /// <summary>
    /// The rows of <typeparamref name="T"/> that belong to <paramref name="siteId"/>: admin
    /// operations on a site named in the route, and background work on one site it has chosen.
    /// </summary>
    /// <remarks>
    /// The bypass and its <c>SiteId</c> predicate stay together here. <c>FormsIsolationProbe</c>
    /// proves another site's rows are never returned.
    /// </remarks>
    internal IQueryable<T> ForSite<T>(Guid siteId)
        where T : class, ISiteOwned =>
        Set<T>()
            .AcrossAllSites("Operation on a site named explicitly: in the admin route (001 FR-011b), or chosen by background work.")
            .Where(e => e.SiteId == siteId);

    /// <summary>
    /// Every form of any site with a retention period (018 FR-023a): the sweep's starting point.
    /// </summary>
    /// <remarks>
    /// One of the module's cross-site reads, with the notification worker's two: background
    /// work has no site and must first learn which sites have work. Each form is then swept
    /// through <see cref="ForSite{T}"/> on its own site. <c>FormsSweepTests</c> covers it.
    /// </remarks>
    internal async Task<List<(Guid SiteId, string FormId, int Days)>> FormsWithRetentionAsync(CancellationToken cancellationToken)
    {
        var forms = await Set<FormRecord>()
            .AcrossAllSites("The retention sweep first finds which forms keep submissions for a limited time; each is then swept on its own site (018 FR-023a).")
            .AsNoTracking()
            .Where(f => f.RetentionDays != null)
            .Select(f => new { f.SiteId, f.FormId, Days = f.RetentionDays!.Value })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. forms.Select(f => (f.SiteId, f.FormId, f.Days))];
    }

    /// <summary>
    /// Claims the next due notification of any site (018 research FS-17): marks it sending under
    /// a lease and counts the attempt, in one statement that commits on its own. Null when none
    /// is due.
    /// </summary>
    /// <remarks>
    /// The notification worker's cross-site read, the module's other one besides the sweep's:
    /// background work has no site. <c>FOR UPDATE SKIP LOCKED</c> lets several instances claim
    /// side by side without ever taking the same delivery. The worker then reads the submission
    /// and its form through <see cref="ForSite{T}"/> on the claimed site.
    /// </remarks>
    internal async Task<(Guid SubmissionId, Guid SiteId)?> ClaimNextDeliveryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var leaseUntil = now + NotificationDelivery.Lease;
        var claimed = await Database.SqlQuery<ClaimedDelivery>($"""
            UPDATE forms.notification_deliveries
            SET state = 'sending', attempts = attempts + 1, lease_until_utc = {leaseUntil}, next_attempt_at_utc = NULL
            WHERE submission_id = (
                SELECT submission_id FROM forms.notification_deliveries
                WHERE state = 'pending' AND next_attempt_at_utc <= {now}
                ORDER BY next_attempt_at_utc
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING submission_id AS "SubmissionId", site_id AS "SiteId"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return claimed is [var one] ? (one.SubmissionId, one.SiteId) : null;
    }

    /// <summary>
    /// Fails every delivery whose lease ran out, on any site, as interrupted, and shows it on
    /// its submission (FS-17): its message may have left, so it is never sent again. Returns
    /// how many.
    /// </summary>
    /// <remarks>The notification worker's, with <see cref="ClaimNextDeliveryAsync"/>.</remarks>
    internal Task<int> FailExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"""
            WITH expired AS (
                UPDATE forms.notification_deliveries
                SET state = 'failed', last_error = {NotificationDelivery.InterruptedReason}, lease_until_utc = NULL, next_attempt_at_utc = NULL
                WHERE state = 'sending' AND lease_until_utc < {now}
                RETURNING submission_id)
            UPDATE forms.submissions s SET notification_state = 'failed'
            FROM expired e WHERE s.id = e.submission_id
            """, cancellationToken);

    /// <summary>
    /// Counts one submit request of a client on a site in its current minute and hour windows,
    /// and returns both counts (018 research FS-13).
    /// </summary>
    /// <remarks>
    /// One atomic upsert, so instances share the counts without a cache. Raw SQL bypasses the
    /// site filter, so the site is stated in the statement itself; <c>SpamAndRateLimitTests</c>
    /// proves another site's windows are untouched.
    /// </remarks>
    internal async Task<(int Minute, int Hour)> CountRequestAsync(
        Guid siteId, byte[] clientKey, DateTimeOffset minuteStart, DateTimeOffset hourStart, CancellationToken cancellationToken)
    {
        var counts = await Database.SqlQuery<WindowCount>($"""
            INSERT INTO forms.rate_windows AS w (site_id, client_key, window_kind, window_start_utc, count)
            VALUES ({siteId}, {clientKey}, 'minute', {minuteStart}, 1), ({siteId}, {clientKey}, 'hour', {hourStart}, 1)
            ON CONFLICT (site_id, client_key, window_kind, window_start_utc) DO UPDATE SET count = w.count + 1
            RETURNING w.window_kind AS "Kind", w.count AS "Count"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return (counts.Single(c => c.Kind == RateWindowKinds.Minute).Count, counts.Single(c => c.Kind == RateWindowKinds.Hour).Count);
    }

    /// <summary>
    /// Adds one to a form's spam counter of the day (018 FR-019), when the site has that form:
    /// a request naming a form that never existed counts nowhere, so made-up ids cannot grow
    /// the table.
    /// </summary>
    /// <remarks>The site is stated in the statement, as for <see cref="CountRequestAsync"/>.</remarks>
    internal Task<int> CountSpamAsync(Guid siteId, string formId, DateOnly day, string kind, CancellationToken cancellationToken)
    {
        var kept = kind == SpamCounterKinds.TrappedKept ? 1 : 0;
        var dropped = kind == SpamCounterKinds.TrappedDropped ? 1 : 0;
        var limited = kind == SpamCounterKinds.RateLimited ? 1 : 0;
        return Database.ExecuteSqlAsync($"""
            INSERT INTO forms.spam_counters AS c (site_id, form_id, day, trapped_kept, trapped_dropped, rate_limited)
            SELECT {siteId}, {formId}, {day}, {kept}, {dropped}, {limited}
            WHERE EXISTS (SELECT 1 FROM forms.forms f WHERE f.site_id = {siteId} AND f.form_id = {formId})
            ON CONFLICT (site_id, form_id, day) DO UPDATE SET
                trapped_kept = c.trapped_kept + EXCLUDED.trapped_kept,
                trapped_dropped = c.trapped_dropped + EXCLUDED.trapped_dropped,
                rate_limited = c.rate_limited + EXCLUDED.rate_limited
            """, cancellationToken);
    }

    /// <summary>
    /// Holds a transaction-scoped advisory lock on a fingerprint's first 8 bytes (018 research
    /// FS-14), so two instances receiving the same submission at once check for each other's in
    /// turn. Released at commit or rollback.
    /// </summary>
    internal Task LockFingerprintAsync(byte[] fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var key = BitConverter.ToInt64(fingerprint, 0);
        return Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> spam submissions of any site received before
    /// <paramref name="before"/>, and says which site and form each was (018 FR-016a; research
    /// FS-17).
    /// </summary>
    /// <remarks>
    /// The sweep's maintenance deletes are cross-site by nature: spam, rate windows and counters
    /// expire by age on every site alike. Each returns or names nothing but counts, which the
    /// sweep records per site and form.
    /// </remarks>
    internal Task<List<PurgedSubmission>> PurgeSpamAsync(DateTimeOffset before, int batchSize, CancellationToken cancellationToken) =>
        Database.SqlQuery<PurgedSubmission>($"""
            DELETE FROM forms.submissions
            WHERE id IN (
                SELECT id FROM forms.submissions
                WHERE is_spam AND received_at_utc < {before}
                LIMIT {batchSize})
            RETURNING site_id AS "SiteId", form_id AS "FormId"
            """).ToListAsync(cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> rate windows of any site that started before <paramref name="before"/>.</summary>
    /// <remarks>The sweep's, with <see cref="PurgeSpamAsync"/>.</remarks>
    internal Task<int> DeleteRateWindowsAsync(DateTimeOffset before, int batchSize, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"""
            DELETE FROM forms.rate_windows
            WHERE ctid IN (SELECT ctid FROM forms.rate_windows WHERE window_start_utc < {before} LIMIT {batchSize})
            """, cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> spam counters of any site older than <paramref name="before"/>.</summary>
    /// <remarks>The sweep's, with <see cref="PurgeSpamAsync"/>.</remarks>
    internal Task<int> DeleteSpamCountersAsync(DateOnly before, int batchSize, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"""
            DELETE FROM forms.spam_counters
            WHERE ctid IN (SELECT ctid FROM forms.spam_counters WHERE day < {before} LIMIT {batchSize})
            """, cancellationToken);

    /// <summary>Makes the open transaction read-only, so the export cannot change what it reads (FS-16). Reads no rows.</summary>
    internal Task MakeTransactionReadOnlyAsync(CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"SET TRANSACTION READ ONLY", cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<FormRecord>(entity =>
        {
            entity.ToTable("forms", Schema, t =>
                t.HasCheckConstraint("ck_forms_retention_days", "retention_days IS NULL OR retention_days IN (30, 90, 180, 365)"));

            entity.HasKey(e => new { e.SiteId, e.FormId });
            entity.Property(e => e.FormId).HasMaxLength(FormLimits.MaxFormIdLength);
            entity.Property(e => e.Name).HasMaxLength(FormLimits.MaxNameLength).IsRequired();
            entity.Property(e => e.Recipients).HasColumnType("text[]").IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(FormLimits.MaxSubjectLength);
            entity.Property(e => e.ReplyFieldId).HasMaxLength(FormLimits.FieldIdLength);
            entity.Property(e => e.Fields).HasColumnType("jsonb").IsRequired();

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<FormPlacement>(entity =>
        {
            entity.ToTable("form_placements", Schema, t =>
                t.HasCheckConstraint("ck_form_placements_subject_kind", "subject_kind IN ('page', 'component')"));

            entity.HasKey(e => new { e.SiteId, e.SubjectKind, e.SubjectId, e.FormId });
            entity.Property(e => e.SubjectKind).HasMaxLength(10);
            entity.Property(e => e.FormId).HasMaxLength(FormLimits.MaxFormIdLength);
            entity.HasIndex(e => new { e.SiteId, e.FormId });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<Submission>(entity =>
        {
            entity.ToTable("submissions", Schema, t =>
                t.HasCheckConstraint(
                    "ck_submissions_notification_state",
                    "notification_state IN ('not_needed', 'not_sent', 'pending', 'sent', 'failed', 'none')"));

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.FormId).HasMaxLength(FormLimits.MaxFormIdLength).IsRequired();
            entity.Property(e => e.FormName).HasMaxLength(FormLimits.MaxNameLength).IsRequired();
            entity.Property(e => e.PagePath).HasMaxLength(Submission.MaxPagePathLength).IsRequired();
            entity.Property(e => e.Values).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.SearchText).HasColumnType("text").IsRequired();
            entity.Property(e => e.Fingerprint).HasColumnType("bytea").IsRequired();
            entity.Property(e => e.NotificationState).HasMaxLength(12).IsRequired();

            // Research FS-15: the default list and unread counts, form filters and retention,
            // and the duplicate check.
            entity.HasIndex(e => new { e.SiteId, e.IsSpam, e.ReceivedAtUtc, e.Id }).IsDescending(false, false, true, true);
            entity.HasIndex(e => new { e.SiteId, e.FormId, e.ReceivedAtUtc }).IsDescending(false, false, true);
            entity.HasIndex(e => new { e.SiteId, e.FormId, e.Fingerprint, e.ReceivedAtUtc });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("notification_deliveries", Schema, t =>
            {
                t.HasCheckConstraint("ck_notification_deliveries_state", "state IN ('pending', 'sending', 'sent', 'failed')");
                t.HasCheckConstraint("ck_notification_deliveries_attempts", $"attempts BETWEEN 0 AND {NotificationDelivery.MaxAttempts}");
            });

            entity.HasKey(e => e.SubmissionId);
            entity.Property(e => e.State).HasMaxLength(10).IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(NotificationDelivery.MaxErrorLength);

            entity.HasOne<Submission>().WithOne().HasForeignKey<NotificationDelivery>(e => e.SubmissionId).OnDelete(DeleteBehavior.Cascade);

            // The worker's claim: due pending deliveries and expired leases, across sites.
            entity.HasIndex(e => new { e.State, e.NextAttemptAtUtc });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<SpamCounter>(entity =>
        {
            entity.ToTable("spam_counters", Schema);

            entity.HasKey(e => new { e.SiteId, e.FormId, e.Day });
            entity.Property(e => e.FormId).HasMaxLength(FormLimits.MaxFormIdLength);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<RateWindow>(entity =>
        {
            entity.ToTable("rate_windows", Schema, t =>
                t.HasCheckConstraint("ck_rate_windows_window_kind", "window_kind IN ('minute', 'hour')"));

            entity.HasKey(e => new { e.SiteId, e.ClientKey, e.WindowKind, e.WindowStartUtc });
            entity.Property(e => e.ClientKey).HasColumnType("bytea");
            entity.Property(e => e.WindowKind).HasMaxLength(6);
            entity.HasIndex(e => e.WindowStartUtc);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<SubmissionLogEntry>(entity =>
        {
            entity.ToTable("submission_log", Schema, t =>
                t.HasCheckConstraint(
                    "ck_submission_log_action",
                    "action IN ('export', 'delete', 'restore', 'retention_purge', 'spam_purge')"));

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.Action).HasMaxLength(20).IsRequired();
            entity.Property(e => e.FormId).HasMaxLength(FormLimits.MaxFormIdLength);
            entity.Property(e => e.Filters).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.SiteId, e.AtUtc });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Every module context carries the outbox (003 analysis U1). Forms publishes no events;
        // its processed_events table deduplicates the registry handler.
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>A claimed delivery: which submission, on which site.</summary>
internal sealed record ClaimedDelivery(Guid SubmissionId, Guid SiteId);

/// <summary>One window's count after a request was counted in it.</summary>
internal sealed record WindowCount(string Kind, int Count);

/// <summary>A deleted submission: which site and form it was, for the log's counts.</summary>
internal sealed record PurgedSubmission(Guid SiteId, string FormId);
