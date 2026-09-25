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
            });

            entity.HasKey(e => e.Id).HasName("pk_media_files");

            // Random v4 ids assigned by the upload handler, never by the database.
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.FileName).HasMaxLength(MediaFileName.MaxLength).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(MediaFile.MaxContentTypeLength).IsRequired();
            entity.Property(e => e.Extension).HasMaxLength(MediaFile.MaxExtensionLength).IsRequired();
            entity.Property(e => e.StorageKey).HasMaxLength(MediaFile.MaxStorageKeyLength).IsRequired();
            entity.Ignore(e => e.Type);

            // One row per stored object; the orphan sweep compares keys against it (M-15).
            entity.HasIndex(e => e.StorageKey).IsUnique().HasDatabaseName("ux_media_files_storage_key");

            // Keyset listing, newest first, and the per-site count (M-16, SC-009a).
            entity
                .HasIndex(e => new { e.SiteId, e.UploadedAtUtc, e.Id })
                .IsDescending(false, true, true)
                .HasDatabaseName("ix_media_files_site_listing");

            // FR-013. Declared here, against CurrentSiteId, never through a captured
            // ISiteContext — see SiteQueryFilterGuidance.
            entity.HasQueryFilter(f => f.SiteId == CurrentSiteId);
        });

        // Every module context carries the outbox: OutboxRetentionService reads
        // Set<OutboxMessage>() from each of them (003 analysis U1).
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }
}
