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

    internal DbSet<ItemSlug> ItemSlugs => Set<ItemSlug>();

    internal DbSet<ItemReference> ItemReferences => Set<ItemReference>();

    internal DbSet<ItemMediaUse> ItemMedia => Set<ItemMediaUse>();

    internal DbSet<ItemVersion> ItemVersions => Set<ItemVersion>();

    internal DbSet<ItemSchedule> ItemSchedules => Set<ItemSchedule>();

    internal DbSet<ItemHistoryEntry> ItemHistory => Set<ItemHistoryEntry>();

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

    /// <summary>
    /// The collections, of any site, that hold items not yet indexed by
    /// <paramref name="indexVersion"/> (015 research IT-11, IT-22).
    /// </summary>
    /// <remarks>
    /// The module's second and last <c>AcrossAllSites</c> call, kept here beside
    /// <see cref="ForSite{T}"/>: background work has no site, and must first learn which sites
    /// have work. It returns ids only; each collection is then processed through
    /// <see cref="ForSite{T}"/> on its own site. <c>CmsSiteFilterTests</c> covers it.
    /// </remarks>
    internal Task<List<SiteCollection>> CollectionsToIndexAsync(int indexVersion, CancellationToken cancellationToken) =>
        Set<Item>()
            .AcrossAllSites("Background indexing first finds which collections have work; each is then processed on its own site (015 IT-22).")
            .AsNoTracking()
            .Where(i => i.IndexVersion < indexVersion)
            .Select(i => new SiteCollection(i.SiteId, i.CollectionId))
            .Distinct()
            .ToListAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // Item slugs compare ignoring case (015 research IT-03), as page slugs do.
        modelBuilder.HasPostgresExtension("citext");

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
            entity.ToTable("items", Schema, t =>
            {
                t.HasCheckConstraint("ck_items_status", "status IN ('draft', 'published', 'changed')");
                t.HasCheckConstraint("ck_items_revision", "revision >= 1");
                t.HasCheckConstraint("ck_items_published_slug", "(published IS NULL) = (published_slug IS NULL)");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Draft).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Published).HasColumnType("jsonb");
            entity.Property(e => e.Slug).HasMaxLength(SlugRules.MaxLength).IsRequired();
            entity.Property(e => e.PublishedSlug).HasMaxLength(SlugRules.MaxLength);
            entity.Property(e => e.Status).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Revision).IsConcurrencyToken();

            // 015 analysis I2: a title may be 10,000 characters, so the name is text and is
            // sorted and indexed on its first 256 (research IT-14).
            entity.Property(e => e.Name).HasColumnType("text");
            entity.Property(e => e.SearchText).HasColumnType("text").IsRequired();
            entity.Property(e => e.Attention).HasColumnType("jsonb");
            entity.Ignore(e => e.NewHistory);
            entity.Ignore(e => e.IsPublished);
            entity.Ignore(e => e.DomainEvents);

            entity.HasOne<Collection>().WithMany().HasForeignKey(e => e.CollectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CollectionId, e.Id });
            entity.HasIndex(e => new { e.CollectionId, e.UpdatedAtUtc, e.Id }).IsDescending(false, true, false);
            entity.HasIndex(e => new { e.CollectionId, e.Slug });
            entity.HasIndex(e => new { e.CollectionId, e.Status });
            entity.HasIndex(e => e.CollectionId)
                .HasDatabaseName("ix_items_collection_id_attention")
                .HasFilter("attention IS NOT NULL");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Research IT-03: one key over every draft and published slug of a collection, so the
        // database refuses a taken slug even when two saves race.
        modelBuilder.Entity<ItemSlug>(entity =>
        {
            entity.ToTable("item_slugs", Schema);

            entity.HasKey(e => new { e.CollectionId, e.Slug });
            entity.Property(e => e.Slug).HasColumnType("citext").HasMaxLength(SlugRules.MaxLength);

            entity.HasOne<Collection>().WithMany().HasForeignKey(e => e.CollectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ItemId);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Research IT-10: NO ACTION on the target refuses deleting an item something refers to,
        // even when the reference was added concurrently, while a whole-collection delete still
        // works: the cascade removes both ends in one statement, and NO ACTION is checked at the
        // end of it, unlike RESTRICT.
        modelBuilder.Entity<ItemReference>(entity =>
        {
            entity.ToTable("item_references", Schema, t =>
            {
                t.HasCheckConstraint("ck_item_references_not_self", "source_item_id <> target_item_id");
                t.HasCheckConstraint("ck_item_references_somewhere", "in_draft OR in_published OR in_scheduled");
            });

            entity.HasKey(e => new { e.SourceItemId, e.FieldId, e.TargetItemId });
            entity.Property(e => e.FieldId).HasMaxLength(8);

            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.SourceItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.TargetItemId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(e => e.TargetItemId);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Research IT-15: the media library reads items' usage from here. No foreign key to the
        // media file: that table belongs to the Media module.
        modelBuilder.Entity<ItemMediaUse>(entity =>
        {
            entity.ToTable("item_media", Schema);

            entity.HasKey(e => new { e.ItemId, e.MediaId });

            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.SiteId, e.MediaId });

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<ItemVersion>(entity =>
        {
            entity.ToTable("item_versions", Schema, t =>
                t.HasCheckConstraint("ck_item_versions_kind", "kind IN ('publish', 'scheduled', 'rollback')"));

            entity.HasKey(e => new { e.ItemId, e.Number });
            entity.Property(e => e.Values).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(SlugRules.MaxLength).IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(10).IsRequired();

            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Research IT-09: at most one pending schedule per item and action.
        modelBuilder.Entity<ItemSchedule>(entity =>
        {
            entity.ToTable("item_schedules", Schema, t =>
            {
                t.HasCheckConstraint("ck_item_schedules_action", "action IN ('publish', 'unpublish')");
                t.HasCheckConstraint("ck_item_schedules_state", "state IN ('pending', 'done', 'cancelled', 'failed')");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Action).HasMaxLength(10).IsRequired();
            entity.Property(e => e.State).HasMaxLength(10).IsRequired();
            entity.Property(e => e.CapturedValues).HasColumnType("jsonb");
            entity.Property(e => e.CapturedSlug).HasMaxLength(SlugRules.MaxLength);
            entity.Property(e => e.FailureCode).HasMaxLength(64);
            entity.Ignore(e => e.IsPending);

            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ItemId, e.Action })
                .IsUnique()
                .HasFilter("state = 'pending'");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<ItemHistoryEntry>(entity =>
        {
            entity.ToTable("item_history", Schema);

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).HasMaxLength(24).IsRequired();
            entity.Property(e => e.ScheduleAction).HasMaxLength(10);
            entity.Property(e => e.FailureCode).HasMaxLength(64);

            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ItemId, e.Id });

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
