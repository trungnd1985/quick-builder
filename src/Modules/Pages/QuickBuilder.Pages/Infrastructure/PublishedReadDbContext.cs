using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The public path's view of Pages data: read-only, published data only (012 research PR-06).
/// </summary>
/// <remarks>
/// Its connection is built with <c>ReadOnlyConnection.From</c>, so PostgreSQL refuses any
/// write in its sessions; <see cref="SaveChanges()"/> throws as well, so a mistake fails in
/// review and in tests long before it reaches the database. It maps only the columns the
/// renderer needs, from tables <see cref="PagesDbContext"/> owns and migrates; this context
/// is never migrated. Every row is site-owned and filtered by the resolved site, like every
/// other context (constitution Principle V).
/// </remarks>
internal sealed class PublishedReadDbContext(DbContextOptions<PublishedReadDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal DbSet<PageRow> Pages => Set<PageRow>();

    internal DbSet<PublicationRow> Publications => Set<PublicationRow>();

    internal DbSet<VersionRow> Versions => Set<VersionRow>();

    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    public override int SaveChanges() => throw ReadOnly();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(PagesDbContext.Schema);

        modelBuilder.Entity<PageRow>(entity =>
        {
            entity.ToTable("pages");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PublicationRow>(entity =>
        {
            entity.ToTable("publications");
            entity.HasKey(e => new { e.SubjectKind, e.SubjectId });
            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<VersionRow>(entity =>
        {
            entity.ToTable("published_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static InvalidOperationException ReadOnly() =>
        new("The published-content context is read-only (constitution Principle VII). Write through PagesDbContext.");
}

/// <summary>The columns of <c>pages.pages</c> the public path reads.</summary>
internal sealed class PageRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Path { get; set; } = string.Empty;

    public bool IsHome { get; set; }
}

/// <summary>The columns of <c>pages.publications</c> the public path reads.</summary>
internal sealed class PublicationRow : ISiteOwned
{
    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public Guid SiteId { get; set; }

    public Guid? LiveVersionId { get; set; }

    public long Generation { get; set; }
}

/// <summary>The columns of <c>pages.published_versions</c> the public path reads.</summary>
internal sealed class VersionRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;

    public string? Title { get; set; }
}
