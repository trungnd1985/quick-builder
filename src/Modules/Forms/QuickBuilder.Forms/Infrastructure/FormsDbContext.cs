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
