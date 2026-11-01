using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// The Editor module's data context. Owns the <c>editor</c> schema and nothing else (001 FR-022).
/// </summary>
/// <remarks>
/// The token set is site-owned and filtered against <see cref="CurrentSiteId"/>. Every Editor
/// endpoint is on the administrative surface, where no site is resolved and the filter
/// therefore matches nothing, so handlers read through <see cref="ForSite{T}"/>, as Pages does
/// (010 research P-15).
/// </remarks>
internal sealed class EditorDbContext(DbContextOptions<EditorDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "editor";

    internal DbSet<DesignTokenSet> DesignTokenSets => Set<DesignTokenSet>();

    internal DbSet<DesignPublication> DesignPublications => Set<DesignPublication>();

    internal DbSet<DesignVersion> DesignVersions => Set<DesignVersion>();

    internal DbSet<EditingSession> EditingSessions => Set<EditingSession>();

    internal DbSet<EditingTakeover> EditingTakeovers => Set<EditingTakeover>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// The site the query filter compares against, referenced through <c>this</c> for the
    /// reason <c>SiteQueryFilterGuidance</c> documents. <see cref="Guid.Empty"/> when no site is
    /// resolved, which fails closed.
    /// </summary>
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    /// <summary>
    /// The rows of <typeparamref name="T"/> that belong to <paramref name="siteId"/>, on the
    /// administrative surface.
    /// </summary>
    /// <remarks>
    /// The module's only <c>AcrossAllSites</c> call: the admin surface has no resolved site, so
    /// every Editor endpoint names its site in the route and has already checked the caller
    /// may see it. The bypass and its <c>SiteId</c> predicate stay together here.
    /// <c>EditorIsolationProbe</c> proves another site's token set is never returned.
    /// </remarks>
    internal IQueryable<T> ForSite<T>(Guid siteId)
        where T : class, ISiteOwned =>
        Set<T>()
            .AcrossAllSites("Admin operation on a site named explicitly in the route (001 FR-011b).")
            .Where(e => e.SiteId == siteId);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DesignTokenSet>(entity =>
        {
            entity.ToTable("design_token_sets", Schema, t =>
                t.HasCheckConstraint("ck_design_token_sets_schema_version", $"schema_version = {DesignTokens.CurrentVersion}"));

            entity.HasKey(e => e.SiteId);
            entity.Property(e => e.SiteId).ValueGeneratedNever();
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // 012 research PR-17: the published design, beside the draft token set.
        modelBuilder.Entity<DesignPublication>(entity =>
        {
            entity.ToTable("design_publications", Schema, t =>
                t.HasCheckConstraint("ck_design_publications_numbers", "live_number >= 1 AND next_number > live_number"));

            entity.HasKey(e => e.SiteId);
            entity.Property(e => e.SiteId).ValueGeneratedNever();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<DesignVersion>(entity =>
        {
            entity.ToTable("design_versions", Schema, t =>
            {
                t.HasCheckConstraint("ck_design_versions_kind", "kind IN ('publish', 'rollback')");
                t.HasCheckConstraint("ck_design_versions_number", "number >= 1");
            });

            entity.HasKey(e => new { e.SiteId, e.Number });
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(16);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // 023 data-model §1.1: one row per open editor tab. The partial unique index is what
        // lets at most one session hold a document's lock, on every instance (FR-010, CE-02).
        modelBuilder.Entity<EditingSession>(entity =>
        {
            entity.ToTable("editing_sessions", Schema, t =>
            {
                t.HasCheckConstraint("ck_editing_sessions_kind", "document_kind IN ('page', 'component', 'item')");
                t.HasCheckConstraint("ck_editing_sessions_collection", "(document_kind = 'item') = (collection_id IS NOT NULL)");
                t.HasCheckConstraint(
                    "ck_editing_sessions_lock",
                    "holds_lock = (lock_taken_at IS NOT NULL) AND holds_lock = (last_activity_at IS NOT NULL)");
                t.HasCheckConstraint(
                    "ck_editing_sessions_lost",
                    "(lost_lock_reason IS NULL AND lost_lock_at IS NULL AND lost_lock_to_user_id IS NULL)"
                    + " OR (lost_lock_reason IN ('takeover', 'moved') AND lost_lock_at IS NOT NULL AND lost_lock_to_user_id IS NOT NULL)");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.DocumentKind).HasMaxLength(16);
            entity.Property(e => e.LostLockReason).HasMaxLength(16);

            // Two indexes on the same columns, so each is named: EF merges unnamed ones.
            entity.HasIndex(e => new { e.SiteId, e.DocumentKind, e.DocumentId }, "ux_editing_sessions_holder")
                .IsUnique()
                .HasFilter("holds_lock");
            entity.HasIndex(e => new { e.SiteId, e.DocumentKind, e.DocumentId }, "ix_editing_sessions_document");
            entity.HasIndex(e => e.LastSeenAt);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<EditingTakeover>(entity =>
        {
            entity.ToTable("editing_takeovers", Schema, t =>
                t.HasCheckConstraint("ck_editing_takeovers_kind", "document_kind IN ('page', 'component', 'item')"));

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.DocumentKind).HasMaxLength(16);
            entity.HasIndex(e => new { e.SiteId, e.TakenAt });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Every module context carries the outbox (003 analysis U1). Since 012 it carries
        // Editor.DesignPublished.
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }
}
