using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The public path's view of Cms data: read-only, published data only (016 research TB-08, as
/// 012 research PR-06 does for pages).
/// </summary>
/// <remarks>
/// Its connection is built with <c>ReadOnlyConnection.From</c>, so PostgreSQL refuses any write
/// in its sessions, and <see cref="SaveChanges()"/> throws as well. It maps only what visitors
/// may see, from tables <see cref="CmsDbContext"/> owns and migrates; this context is never
/// migrated. Items are visible only with published data, and only their published columns are
/// mapped at all, so no draft value can reach a page. Every row is site-owned and filtered by
/// the resolved site (constitution Principle V).
/// </remarks>
internal sealed class CmsPublishedReadDbContext(DbContextOptions<CmsPublishedReadDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal DbSet<PublishedItemRow> Items => Set<PublishedItemRow>();

    internal DbSet<PublishedReferenceRow> References => Set<PublishedReferenceRow>();

    internal DbSet<CollectionSchemaRow> Collections => Set<CollectionSchemaRow>();

    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    public override int SaveChanges() => throw ReadOnly();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(CmsDbContext.Schema);

        modelBuilder.Entity<PublishedItemRow>(entity =>
        {
            entity.ToTable("items");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Published).HasColumnName("published").HasColumnType("jsonb");
            entity.Property(e => e.PublishedText).HasColumnName("published_text").HasColumnType("jsonb");
            entity.Property(e => e.LastPublishedAtUtc).HasColumnName("published_at_utc");

            // Only items with published data exist for the public path (Principle VII).
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId && e.PublishedSlug != null);
            entity.HasQueryFilter(QueryFilters.Trash, e => e.TrashedAtUtc == null);
        });

        modelBuilder.Entity<PublishedReferenceRow>(entity =>
        {
            entity.ToTable("item_references");
            entity.HasKey(e => new { e.SourceItemId, e.FieldId, e.TargetItemId });

            // Only references held by published values.
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId && e.InPublished);
        });

        modelBuilder.Entity<CollectionSchemaRow>(entity =>
        {
            entity.ToTable("collections");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Schema).HasColumnType("jsonb");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static InvalidOperationException ReadOnly() =>
        new("The published-items context is read-only (constitution Principle VII). Write through CmsDbContext.");
}

/// <summary>The published columns of <c>cms.items</c>; nothing of the draft.</summary>
internal sealed class PublishedItemRow : ISiteOwned, ITrashable, IDisposable
{
    /// <summary>Set while the item is in the trash; a trashed item is never published (021 TR-07).</summary>
    public DateTimeOffset? TrashedAtUtc { get; set; }

    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid CollectionId { get; set; }

    public string? PublishedSlug { get; set; }

    /// <summary>The published values document; never null for a row this context returns.</summary>
    public JsonDocument? Published { get; set; }

    /// <summary>The published text values, folded for <c>contains</c> (016 research TB-08).</summary>
    public JsonDocument? PublishedText { get; set; }

    public DateTimeOffset? FirstPublishedAtUtc { get; set; }

    public DateTimeOffset? LastPublishedAtUtc { get; set; }

    public long RenderStamp { get; set; }

    public void Dispose()
    {
        Published?.Dispose();
        PublishedText?.Dispose();
    }
}

/// <summary>A reference held by an item's published values (015 <c>item_references</c>).</summary>
internal sealed class PublishedReferenceRow : ISiteOwned
{
    public Guid SourceItemId { get; set; }

    public string FieldId { get; set; } = string.Empty;

    public Guid TargetItemId { get; set; }

    public Guid SiteId { get; set; }

    public bool InPublished { get; set; }
}

/// <summary>A collection's current schema document.</summary>
internal sealed class CollectionSchemaRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Schema { get; set; } = string.Empty;
}
