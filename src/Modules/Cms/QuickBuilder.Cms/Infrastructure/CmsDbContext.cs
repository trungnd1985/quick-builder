using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The Cms module's data context. Owns the <c>cms</c> schema and nothing else (014 data-model §1).
/// </summary>
/// <remarks>
/// Every table is site-owned and filtered against <see cref="CurrentSiteId"/>. Every Cms
/// endpoint is on the administrative surface, where no site is resolved and the filter
/// therefore matches nothing, so handlers read through <see cref="ForSite{T}"/>, as Pages and
/// Editor do. Both item columns are mapped as <c>JsonDocument</c>, so a preview can read one
/// field of every item without loading whole documents (research CS-05, CS-08).
/// </remarks>
internal sealed class CmsDbContext(DbContextOptions<CmsDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "cms";

    internal DbSet<Collection> Collections => Set<Collection>();

    internal DbSet<Item> Items => Set<Item>();

    internal DbSet<CollectionReference> CollectionReferences => Set<CollectionReference>();

    internal DbSet<SchemaChangeEntry> SchemaChanges => Set<SchemaChangeEntry>();

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
    /// every Cms endpoint names its site in the route and has already checked the caller may
    /// see it. The bypass and its <c>SiteId</c> predicate stay together here.
    /// <c>CmsIsolationProbe</c> proves another site's rows are never returned.
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

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.ToTable("collections", Schema, t =>
            {
                t.HasCheckConstraint("ck_collections_schema_version", $"schema_version = {CollectionSchema.CurrentVersion}");
                t.HasCheckConstraint("ck_collections_revision", "revision >= 1");
                t.HasCheckConstraint("ck_collections_counters", "next_field_number >= 1 AND next_option_number >= 1");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasMaxLength(CollectionSchemaLimits.MaxNameLength).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(CollectionSchemaLimits.MaxKeyLength).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(CollectionSchemaLimits.MaxDescriptionLength);
            entity.Property(e => e.Schema).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.Revision).IsConcurrencyToken();
            entity.Ignore(e => e.Details);
            entity.Ignore(e => e.SchemaDocument);
            entity.Ignore(e => e.DomainEvents);

            entity.HasIndex(e => new { e.SiteId, e.Slug }).IsUnique();
            entity.HasIndex(e => new { e.SiteId, e.Name });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.ToTable("items", Schema);

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Draft).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Published).HasColumnType("jsonb");

            entity.HasOne<Collection>().WithMany().HasForeignKey(e => e.CollectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CollectionId, e.Id });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Research CS-11: the database, not a check in code, refuses to delete a collection
        // another one refers to, so a concurrent save cannot slip past it.
        modelBuilder.Entity<CollectionReference>(entity =>
        {
            entity.ToTable("collection_references", Schema, t =>
                t.HasCheckConstraint("ck_collection_references_not_self", "source_collection_id <> target_collection_id"));

            entity.HasKey(e => new { e.SourceCollectionId, e.FieldId });
            entity.Property(e => e.FieldId).HasMaxLength(8);

            entity.HasOne<Collection>().WithMany().HasForeignKey(e => e.SourceCollectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Collection>().WithMany().HasForeignKey(e => e.TargetCollectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.TargetCollectionId);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<SchemaChangeEntry>(entity =>
        {
            entity.ToTable("schema_changes", Schema);

            entity.HasKey(e => new { e.CollectionId, e.Revision });
            entity.Property(e => e.Summary).HasColumnType("jsonb").IsRequired();

            entity.HasOne<Collection>().WithMany().HasForeignKey(e => e.CollectionId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Every module context carries the outbox (003 analysis U1): Cms.CollectionCreated,
        // Cms.CollectionChanged and Cms.CollectionDeleted (research CS-14).
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }
}
