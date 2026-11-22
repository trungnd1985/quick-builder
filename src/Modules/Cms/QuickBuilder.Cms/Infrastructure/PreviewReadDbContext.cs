using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The preview path's view of Cms data: item drafts and retained versions, read-only (022
/// research PV-02).
/// </summary>
/// <remarks>
/// Separate from <see cref="CmsPublishedReadDbContext"/>, which stays "published data only"
/// (constitution Principle VII). Built on <c>ReadOnlyConnection.From</c>, never migrated —
/// <see cref="CmsDbContext"/> owns the tables — and <see cref="SaveChanges()"/> throws. The site
/// and trash filters apply as everywhere.
/// </remarks>
internal sealed class PreviewReadDbContext(DbContextOptions<PreviewReadDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal DbSet<PreviewItemRow> Items => Set<PreviewItemRow>();

    internal DbSet<PreviewItemVersionRow> Versions => Set<PreviewItemVersionRow>();

    internal DbSet<PreviewItemScheduleRow> Schedules => Set<PreviewItemScheduleRow>();

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

        modelBuilder.Entity<PreviewItemRow>(entity =>
        {
            entity.ToTable("items");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Draft).HasColumnType("jsonb");
            entity.Property(e => e.Attention).HasColumnType("jsonb");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
            entity.HasQueryFilter(QueryFilters.Trash, e => e.TrashedAtUtc == null);
        });

        modelBuilder.Entity<PreviewItemVersionRow>(entity =>
        {
            entity.ToTable("item_versions");
            entity.HasKey(e => new { e.ItemId, e.Number });
            entity.Property(e => e.Values).HasColumnType("jsonb");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewItemScheduleRow>(entity =>
        {
            entity.ToTable("item_schedules");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
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
        new("The preview context is read-only (022 research PV-02). Write through CmsDbContext.");
}

/// <summary>The columns of <c>cms.items</c> a preview reads.</summary>
internal sealed class PreviewItemRow : ISiteOwned, ITrashable, IDisposable
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid CollectionId { get; set; }

    public JsonDocument Draft { get; set; } = null!;

    public string Slug { get; set; } = string.Empty;

    public string? PublishedSlug { get; set; }

    public JsonDocument? Attention { get; set; }

    public DateTimeOffset DraftChangedAtUtc { get; set; }

    public Guid? DraftChangedBy { get; set; }

    public DateTimeOffset? FirstPublishedAtUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public DateTimeOffset? TrashedAtUtc { get; set; }

    public void Dispose()
    {
        Draft?.Dispose();
        Attention?.Dispose();
    }
}

/// <summary>The columns of <c>cms.item_versions</c> a preview reads.</summary>
internal sealed class PreviewItemVersionRow : ISiteOwned, IDisposable
{
    public Guid ItemId { get; set; }

    public int Number { get; set; }

    public Guid SiteId { get; set; }

    public JsonDocument Values { get; set; } = null!;

    public string Slug { get; set; } = string.Empty;

    public Guid PublishedBy { get; set; }

    public DateTimeOffset PublishedAtUtc { get; set; }

    public void Dispose() => Values?.Dispose();
}

/// <summary>The columns of <c>cms.item_schedules</c> a preview reads.</summary>
internal sealed class PreviewItemScheduleRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid ItemId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset DueAtUtc { get; set; }
}
