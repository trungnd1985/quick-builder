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

    internal DbSet<DependencyRow> Dependencies => Set<DependencyRow>();

    /// <summary>017 research SE-10: redirects, read on every request no content answers.</summary>
    internal DbSet<RedirectRow> Redirects => Set<RedirectRow>();

    /// <summary>017 research SE-15: the sitemap generation.</summary>
    internal DbSet<SitemapStateRow> SitemapStates => Set<SitemapStateRow>();

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
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
            entity.HasQueryFilter(QueryFilters.Trash, e => e.TrashedAtUtc == null);
        });

        modelBuilder.Entity<PublicationRow>(entity =>
        {
            entity.ToTable("publications");
            entity.HasKey(e => new { e.SubjectKind, e.SubjectId });
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<VersionRow>(entity =>
        {
            entity.ToTable("published_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<DependencyRow>(entity =>
        {
            entity.ToTable("live_dependencies");
            entity.HasKey(e => new { e.SiteId, e.SubjectKind, e.SubjectId, e.TargetKind, e.TargetId });
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<RedirectRow>(entity =>
        {
            entity.ToTable("redirects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StatusCode).HasColumnType("smallint");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<SitemapStateRow>(entity =>
        {
            entity.ToTable("sitemap_state");
            entity.HasKey(e => e.SiteId);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static InvalidOperationException ReadOnly() =>
        new("The published-content context is read-only (constitution Principle VII). Write through PagesDbContext.");
}

/// <summary>The columns of <c>pages.pages</c> the public path reads.</summary>
internal sealed class PageRow : ISiteOwned, ITrashable
{
    /// <summary>Set while the page is in the trash, where it answers not found (021 TR-03).</summary>
    public DateTimeOffset? TrashedAtUtc { get; set; }

    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Path { get; set; } = string.Empty;

    public bool IsHome { get; set; }

    /// <summary>The collection a template page shows; null for an ordinary page (016 research TB-02).</summary>
    public Guid? TemplateCollectionId { get; set; }
}

/// <summary>The columns of <c>pages.publications</c> the public path reads.</summary>
internal sealed class PublicationRow : ISiteOwned
{
    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public Guid SiteId { get; set; }

    public Guid? LiveVersionId { get; set; }

    public long Generation { get; set; }

    /// <summary>When the live version went live, for the sitemap's last-modified dates (017 SE-14).</summary>
    public DateTimeOffset? LiveSinceUtc { get; set; }

    /// <summary>The live version's page parameters (016 data-model §1.2a).</summary>
    public string[] PageParamNames { get; set; } = [];
}

/// <summary>The columns of <c>pages.live_dependencies</c> the public path reads (016 FR-027).</summary>
internal sealed class DependencyRow : ISiteOwned
{
    public Guid SiteId { get; set; }

    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }
}

/// <summary>The columns of <c>pages.published_versions</c> the public path reads.</summary>
internal sealed class VersionRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;

    public string? Title { get; set; }

    /// <summary>024 NV-09: which subject, and which of its versions, for the title visitors saw last.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public int Number { get; set; }
}

/// <summary>The columns of <c>pages.redirects</c> the public path reads (017 research SE-10).</summary>
internal sealed class RedirectRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string MatchKind { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string SourceQuery { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid? TargetPageId { get; set; }

    public Guid? TargetCollectionId { get; set; }

    public Guid? TargetItemId { get; set; }

    public string? TargetPath { get; set; }

    public string? TargetUrl { get; set; }

    public bool Enabled { get; set; }
}

/// <summary>The columns of <c>pages.sitemap_state</c> the public path reads (017 research SE-15).</summary>
internal sealed class SitemapStateRow : ISiteOwned
{
    public Guid SiteId { get; set; }

    public long Generation { get; set; }
}
