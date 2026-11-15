using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// The Sites module's data context. Owns the <c>sites</c> schema and nothing else
/// (FR-022).
/// </summary>
internal sealed class SitesDbContext(DbContextOptions<SitesDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "sites";

    internal DbSet<Site> Sites => Set<Site>();

    internal DbSet<SiteHostName> HostNames => Set<SiteHostName>();

    /// <summary>020: exports, uploads and imports (data-model §1).</summary>
    internal DbSet<TransferJob> TransferJobs => Set<TransferJob>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>
    /// The site global query filters compare against (FR-013).
    /// </summary>
    /// <remarks>
    /// This must be a property <em>on the context</em>, and the filter expression must
    /// reference it through <c>this</c>. EF Core builds and caches the model once per
    /// context type, so a filter closing over an injected <c>ISiteContext</c> captures
    /// whichever instance happened to build the model — in practice the one from startup
    /// migration, where no site is resolved. Every later request then filters against a
    /// stale empty site and matches nothing.
    /// <para>
    /// Referencing a context property instead makes EF lift it into a query parameter that
    /// is re-evaluated per query, against the context actually running it.
    /// </para>
    /// <para>
    /// <see cref="Guid.Empty"/> when no site is resolved, which fails closed: an
    /// administrative request sees no site-owned rows rather than all of them.
    /// </para>
    /// </remarks>
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    /// <summary>
    /// Makes the next save update the site's own row, so its <c>xmin</c> is checked.
    /// </summary>
    /// <remarks>
    /// Adding or removing a host name changes only <c>site_host_names</c> unless the site
    /// row changes too. <see cref="Site"/> moves <c>UpdatedAtUtc</c> on every change, but
    /// when the clock has not moved (two changes in one tick, or a test clock) EF would see
    /// no difference and skip the row — and with it the concurrency check that keeps the
    /// aggregate's invariants under concurrent changes (research S-05). Marking the column
    /// modified forces the update either way.
    /// </remarks>
    internal void MarkAggregateChanged(Site site) =>
        Entry(site).Property(s => s.UpdatedAtUtc).IsModified = true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // citext: host names are case-insensitive by definition. Putting that in the
        // column beats relying on every caller to lowercase correctly.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<Site>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Keys are assigned by the domain (Guid.CreateVersion7), never by the database.
            // Saying so matters: EF assumes a set, store-generated key means an existing row,
            // so a host name added to a loaded site was sent as an UPDATE matching nothing
            // (found by 002's SiteVisibilityTests; nothing in 001 added a host name to an
            // existing site).
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasMaxLength(Site.MaxNameLength).IsRequired();

            // 009 (data-model.md). Defaults exist for the upgrade: rows written by 003 get
            // them, and the domain always sets every value itself.
            entity.Property(e => e.Scheme).IsRequired().HasDefaultValue(SiteScheme.Https);
            entity.Property(e => e.DefaultLanguage).IsRequired().HasDefaultValue(SupportedLanguages.Default);
            entity.Property(e => e.Status).IsRequired().HasDefaultValue(SiteStatus.Enabled);
            entity
                .Property(e => e.PrimaryHostName)
                .HasColumnType("citext")
                .HasMaxLength(SiteHostName.MaxLength)
                .IsRequired();

            // 017 data-model §1.4 (research SE-07, SE-08). Columns on the site row, so a change
            // moves xmin, which every public cache key carries as the site's version.
            entity.Property(e => e.SearchTitleSuffix).HasMaxLength(SiteSearchRules.MaxTitleSuffixLength);
            entity.Property(e => e.SearchDefaultImage);
            entity.Property(e => e.SearchOrganisationName).HasColumnName("search_org_name").HasMaxLength(SiteSearchRules.MaxOrganisationNameLength);
            entity.Property(e => e.SearchOrganisationLogo).HasColumnName("search_org_logo");
            entity.Property(e => e.SearchDiscouraged).HasDefaultValue(false);
            entity.Property(e => e.SearchRobotsRules).HasMaxLength(SiteSearchRules.MaxRobotsRulesBytes);
            entity.Property(e => e.RedirectOtherHosts).HasDefaultValue(false);
            entity.Property(e => e.SearchUpdatedBy);

            // PostgreSQL's xmin system column: no real column, no migration, and it moves on
            // every update of the row. Every aggregate change updates this row (Site.Touch),
            // so this one token guards the whole aggregate (research S-05).
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            entity.ToTable("sites", Schema, t =>
            {
                t.HasCheckConstraint("ck_sites_scheme", "scheme IN ('https', 'http')");
                t.HasCheckConstraint("ck_sites_status", "status IN ('enabled', 'disabled')");
            });

            entity
                .HasMany(e => e.HostNames)
                .WithOne()
                .HasForeignKey(h => h.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.HostNames).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SiteHostName>(entity =>
        {
            entity.ToTable("site_host_names", Schema);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity
                .Property(e => e.HostName)
                .HasColumnType("citext")
                .HasMaxLength(SiteHostName.MaxLength)
                .IsRequired();

            // Global uniqueness, not per site: resolution is host name -> site, so two
            // sites claiming one host name would be ambiguous. See SiteHostName remarks.
            entity
                .HasIndex(e => e.HostName)
                .IsUnique()
                .HasDatabaseName("ux_site_host_names_host_name");

            entity.HasIndex(e => e.SiteId).HasDatabaseName("ix_site_host_names_site_id");

            // FR-013. Expressed here rather than through a generic helper because the
            // filter has to reference CurrentSiteId on this context — see its remarks for
            // why a captured ISiteContext silently filters against the wrong site.
            entity.HasQueryFilter(QueryFilters.Site, h => h.SiteId == CurrentSiteId);
        });

        // 020 data-model §1. Installation data ([InstallationScoped]): a new-site import has no
        // site until it commits, so there is no query filter, and every read that concerns a
        // site names it explicitly.
        modelBuilder.Entity<TransferJob>(entity =>
        {
            entity.ToTable("transfer_jobs", Schema, t =>
            {
                t.HasCheckConstraint("ck_transfer_jobs_kind", "kind IN ('export', 'import')");
                t.HasCheckConstraint("ck_transfer_jobs_trigger", "trigger IN ('admin', 'cli', 'pre_restore')");
                t.HasCheckConstraint("ck_transfer_jobs_mode", "mode IS NULL OR mode IN ('new_site', 'merge', 'restore')");
                t.HasCheckConstraint("ck_transfer_jobs_policy", "conflict_policy IS NULL OR conflict_policy IN ('replace', 'skip', 'rename')");
                t.HasCheckConstraint("ck_transfer_jobs_percent", "percent BETWEEN 0 AND 100");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).IsRequired();
            entity.Property(e => e.Trigger).IsRequired();
            entity.Property(e => e.State).IsRequired();
            entity.Property(e => e.NewSiteName).HasMaxLength(TransferJob.MaxSiteNameLength);
            entity.Property(e => e.NewSiteHostName).HasColumnType("citext").HasMaxLength(SiteHostName.MaxLength);
            entity.Property(e => e.Manifest).HasColumnType("jsonb");
            entity.Property(e => e.CheckResult).HasColumnType("jsonb");
            entity.Property(e => e.Summary).HasColumnType("jsonb");
            entity.Property(e => e.Report).HasColumnType("jsonb");
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            // One active transfer per site (FR-010). The predicate is TransferJob.HoldsSiteSlot
            // in SQL: a check holds no slot, nor does a restore's own pre-restore export
            // (analysis I1, I2).
            entity
                .HasIndex(e => e.SiteId)
                .IsUnique()
                .HasFilter(TransferStates.HoldingSlotSql)
                .HasDatabaseName("ux_transfer_jobs_active_site");

            entity
                .HasIndex(e => new { e.State, e.CreatedAtUtc })
                .HasFilter("state IN ('queued', 'confirmed')")
                .HasDatabaseName("ix_transfer_jobs_claim");

            entity
                .HasIndex(e => e.ArchiveExpiresAtUtc)
                .HasFilter("archive_key IS NOT NULL")
                .HasDatabaseName("ix_transfer_jobs_expiry");

            entity.HasIndex(e => new { e.SiteId, e.CreatedAtUtc }).HasDatabaseName("ix_transfer_jobs_site");
        });

        modelBuilder.AddOutbox(Schema);

        // Before the index filters below are turned into SQL: they name columns such as
        // completed_at_utc, so the naming pass has to have run first.
        modelBuilder.UseSnakeCaseColumns();

        // Site filters are declared per entity above (see CurrentSiteId).

        base.OnModelCreating(modelBuilder);
    }
}
