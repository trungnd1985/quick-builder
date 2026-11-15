using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// The Media module's data context. Owns the <c>media</c> schema and nothing else (001 FR-022).
/// </summary>
internal sealed class MediaDbContext(DbContextOptions<MediaDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "media";

    internal DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    internal DbSet<MediaFolder> Folders => Set<MediaFolder>();

    internal DbSet<FolderTree> FolderTrees => Set<FolderTree>();

    internal DbSet<VariantClaim> VariantClaims => Set<VariantClaim>();

    internal DbSet<StorageCheck> StorageChecks => Set<StorageCheck>();

    internal DbSet<StorageCheckMissing> StorageCheckMissing => Set<StorageCheckMissing>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>
    /// The site the query filter compares against. A property on the context, referenced
    /// through <c>this</c>, for the reason <c>SitesDbContext.CurrentSiteId</c> documents.
    /// <see cref="Guid.Empty"/> when no site is resolved, which fails closed.
    /// </summary>
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<MediaFile>(entity =>
        {
            entity.ToTable("media_files", Schema, table =>
            {
                table.HasCheckConstraint("ck_media_files_size_positive", "size_bytes > 0");
                table.HasCheckConstraint("ck_media_files_extension", "extension ~ '^[a-z0-9]{1,8}$'");

                // 013 data-model.md §1.1.
                table.HasCheckConstraint("ck_media_files_alt_text_images", "alt_text IS NULL OR content_type LIKE 'image/%'");
                table.HasCheckConstraint(
                    "ck_media_files_dimensions",
                    "(width IS NULL) = (height IS NULL) AND (width IS NULL OR (width > 0 AND height > 0))");
                table.HasCheckConstraint("ck_media_files_content_version", "content_version >= 1");

                // 021 data-model §2: all set or all null; a trashed file is in no folder (TR-05).
                table.HasCheckConstraint(
                    "ck_media_files_trash",
                    "(trashed_at_utc IS NULL) = (trashed_by IS NULL) " +
                    "AND (trashed_at_utc IS NOT NULL OR trashed_folder_id IS NULL) " +
                    "AND (trashed_at_utc IS NULL OR folder_id IS NULL)");
            });

            entity.HasKey(e => e.Id).HasName("pk_media_files");

            // Random v4 ids assigned by the upload handler, never by the database.
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.FileName).HasMaxLength(MediaFileName.MaxLength).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(MediaFile.MaxContentTypeLength).IsRequired();
            entity.Property(e => e.Extension).HasMaxLength(MediaFile.MaxExtensionLength).IsRequired();
            entity.Property(e => e.StorageKey).HasMaxLength(MediaFile.MaxStorageKeyLength).IsRequired();
            entity.Ignore(e => e.Type);

            // 013 data-model.md §1.1.
            entity.Property(e => e.AltText).HasMaxLength(MediaFile.MaxAltTextLength);
            entity.Property(e => e.MetadataState).HasConversion<short>().HasColumnType("smallint");
            entity.Property(e => e.VariantsState).HasConversion<short>().HasColumnType("smallint");
            entity.Property(e => e.SearchTextState).HasConversion<short>().HasColumnType("smallint");
            entity.Property(e => e.ContentVersion).HasDefaultValue(1);
            entity.Property(e => e.SearchText).IsRequired();
            entity.Property(e => e.SortName).HasMaxLength(MediaFileName.MaxLength).IsRequired();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            entity
                .HasOne<MediaFolder>()
                .WithMany()
                .HasForeignKey(e => new { e.SiteId, e.FolderId })
                .HasPrincipalKey(f => new { f.SiteId, f.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_media_files_folder");

            // One row per stored object; the orphan sweep compares keys against it (M-15).
            entity.HasIndex(e => e.StorageKey).IsUnique().HasDatabaseName("ux_media_files_storage_key");

            // Keyset listing, newest first, and the per-site count (M-16, SC-009a).
            entity
                .HasIndex(e => new { e.SiteId, e.UploadedAtUtc, e.Id })
                .IsDescending(false, true, true)
                .HasDatabaseName("ix_media_files_site_listing");

            // 013 MV-03: keyset listing per folder and per sort, and whole-library sorts.
            entity
                .HasIndex(e => new { e.SiteId, e.FolderId, e.UploadedAtUtc, e.Id })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("ix_media_files_folder_uploaded");
            entity.HasIndex(e => new { e.SiteId, e.FolderId, e.SortName, e.Id }).HasDatabaseName("ix_media_files_folder_name");
            entity.HasIndex(e => new { e.SiteId, e.FolderId, e.SizeBytes, e.Id }).HasDatabaseName("ix_media_files_folder_size");
            entity.HasIndex(e => new { e.SiteId, e.SortName, e.Id }).HasDatabaseName("ix_media_files_site_name");
            entity.HasIndex(e => new { e.SiteId, e.SizeBytes, e.Id }).HasDatabaseName("ix_media_files_site_size");

            // 013 MV-10, MV-26: the backfills find their work without scanning the table.
            entity
                .HasIndex(e => new { e.SiteId, e.MetadataState })
                .HasFilter("metadata_state = 0")
                .HasDatabaseName("ix_media_files_metadata_pending");
            entity
                .HasIndex(e => new { e.SiteId, e.SearchTextState })
                .HasFilter("search_text_state = 0")
                .HasDatabaseName("ix_media_files_search_text_provisional");

            // 021 TR-19: the trash list, newest first.
            entity
                .HasIndex(e => new { e.SiteId, e.TrashedAtUtc, e.Id })
                .IsDescending(false, true, false)
                .HasFilter("trashed_at_utc IS NOT NULL")
                .HasDatabaseName("ix_media_files_trash");
            entity.Ignore(e => e.IsTrashed);

            // FR-013. Declared here, against CurrentSiteId, never through a captured
            // ISiteContext — see SiteQueryFilterGuidance.
            entity.HasQueryFilter(QueryFilters.Site, f => f.SiteId == CurrentSiteId);

            // 021 TR-03: a trashed file is deleted for every read but the trash's own.
            entity.HasQueryFilter(QueryFilters.Trash, f => f.TrashedAtUtc == null);
        });

        ConfigureLibrary(modelBuilder);

        // Every module context carries the outbox: OutboxRetentionService reads
        // Set<OutboxMessage>() from each of them (003 analysis U1).
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Folders, variant claims and storage checks (013 data-model.md §1.2 – §1.6).</summary>
    private void ConfigureLibrary(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaFolder>(entity =>
        {
            entity.ToTable("folders", Schema, table =>
                table.HasCheckConstraint("ck_folders_not_own_parent", "parent_id IS NULL OR parent_id <> id"));

            entity.HasKey(e => e.Id).HasName("pk_folders");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasMaxLength(MediaFolder.MaxNameLength).IsRequired();
            entity.Property(e => e.NameKey).HasMaxLength(MediaFolder.MaxNameLength * 3).IsRequired();

            // The target of both RESTRICT foreign keys, so a folder can only contain files and
            // folders of its own site (MV-02).
            entity.HasAlternateKey(e => new { e.SiteId, e.Id }).HasName("ak_folders_site_id");

            entity
                .HasOne<MediaFolder>()
                .WithMany()
                .HasForeignKey(e => new { e.SiteId, e.ParentId })
                .HasPrincipalKey(f => new { f.SiteId, f.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_folders_parent");

            // FR-004: sibling names are unique ignoring case and accents, top level included.
            entity
                .HasIndex(e => new { e.SiteId, e.ParentId, e.NameKey })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName("ux_folders_sibling_name");

            entity.HasQueryFilter(QueryFilters.Site, f => f.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<FolderTree>(entity =>
        {
            entity.ToTable("folder_trees", Schema);
            entity.HasKey(e => e.SiteId).HasName("pk_folder_trees");
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            entity.HasQueryFilter(QueryFilters.Site, t => t.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<VariantClaim>(entity =>
        {
            entity.ToTable("variant_claims", Schema);
            entity.HasKey(e => new { e.FileId, e.ContentVersion, e.Width }).HasName("pk_variant_claims");
            entity.HasQueryFilter(QueryFilters.Site, c => c.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<StorageCheck>(entity =>
        {
            entity.ToTable("storage_checks", Schema, table =>
                table.HasCheckConstraint("ck_storage_checks_status", "status IN ('running', 'completed', 'failed')"));

            entity.HasKey(e => e.Id).HasName("pk_storage_checks");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Trigger).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(16).IsRequired();

            // MV-21: at most one run at a time, across every instance.
            entity
                .HasIndex(e => e.Status)
                .IsUnique()
                .HasFilter("status = 'running'")
                .HasDatabaseName("ux_storage_checks_running");

            entity.HasIndex(e => e.StartedAtUtc).HasDatabaseName("ix_storage_checks_started");
        });

        modelBuilder.Entity<StorageCheckMissing>(entity =>
        {
            entity.ToTable("storage_check_missing", Schema);
            entity.HasKey(e => new { e.CheckId, e.SiteId, e.FileId }).HasName("pk_storage_check_missing");

            entity
                .HasOne<StorageCheck>()
                .WithMany()
                .HasForeignKey(e => e.CheckId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_storage_check_missing_check");

            entity.HasQueryFilter(QueryFilters.Site, m => m.SiteId == CurrentSiteId);
        });
    }
}
