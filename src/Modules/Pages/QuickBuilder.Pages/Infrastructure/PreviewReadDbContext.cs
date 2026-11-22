using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The preview path's view of Pages data: drafts and retained versions, read-only (022 research
/// PV-02).
/// </summary>
/// <remarks>
/// Separate from <see cref="PublishedReadDbContext"/>, which stays "published data only", so not
/// even a reader inside this module can hand a draft to the public path (constitution Principle
/// VII). Its connection is built with <c>ReadOnlyConnection.From</c>, so PostgreSQL refuses any
/// write in its sessions, and <see cref="SaveChanges()"/> throws as well. It is never migrated:
/// <see cref="PagesDbContext"/> owns the tables. The site and trash filters apply as everywhere.
/// </remarks>
internal sealed class PreviewReadDbContext(DbContextOptions<PreviewReadDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal DbSet<PreviewPageRow> Pages => Set<PreviewPageRow>();

    internal DbSet<PreviewDraftRow> PageDrafts => Set<PreviewDraftRow>();

    internal DbSet<PreviewComponentRow> Components => Set<PreviewComponentRow>();

    internal DbSet<PreviewComponentDraftRow> ComponentDrafts => Set<PreviewComponentDraftRow>();

    internal DbSet<PreviewMenuRow> Menus => Set<PreviewMenuRow>();

    internal DbSet<PreviewMenuDraftRow> MenuDrafts => Set<PreviewMenuDraftRow>();

    internal DbSet<PreviewPublicationRow> Publications => Set<PreviewPublicationRow>();

    internal DbSet<PreviewVersionRow> Versions => Set<PreviewVersionRow>();

    internal DbSet<PreviewScheduleRow> Schedules => Set<PreviewScheduleRow>();

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

        modelBuilder.Entity<PreviewPageRow>(entity =>
        {
            entity.ToTable("pages");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
            entity.HasQueryFilter(QueryFilters.Trash, e => e.TrashedAtUtc == null);
        });

        modelBuilder.Entity<PreviewDraftRow>(entity =>
        {
            entity.ToTable("page_drafts");
            entity.HasKey(e => e.PageId);
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewComponentRow>(entity =>
        {
            entity.ToTable("components");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
            entity.HasQueryFilter(QueryFilters.Trash, e => e.TrashedAtUtc == null);
        });

        modelBuilder.Entity<PreviewComponentDraftRow>(entity =>
        {
            entity.ToTable("component_drafts");
            entity.HasKey(e => e.ComponentId);
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewMenuRow>(entity =>
        {
            entity.ToTable("menus");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewMenuDraftRow>(entity =>
        {
            entity.ToTable("menu_drafts");
            entity.HasKey(e => e.MenuId);
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewPublicationRow>(entity =>
        {
            entity.ToTable("publications");
            entity.HasKey(e => new { e.SubjectKind, e.SubjectId });
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewVersionRow>(entity =>
        {
            entity.ToTable("published_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PreviewScheduleRow>(entity =>
        {
            entity.ToTable("publish_schedules");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static InvalidOperationException ReadOnly() =>
        new("The preview context is read-only (022 research PV-02). Write through PagesDbContext.");
}

/// <summary>The columns of <c>pages.pages</c> a preview reads.</summary>
internal sealed class PreviewPageRow : ISiteOwned, ITrashable
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public Guid? TemplateCollectionId { get; set; }

    public DateTimeOffset? TrashedAtUtc { get; set; }
}

/// <summary>The columns of <c>pages.page_drafts</c> a preview reads.</summary>
internal sealed class PreviewDraftRow : ISiteOwned
{
    public Guid PageId { get; set; }

    public Guid SiteId { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}

/// <summary>The columns of <c>pages.components</c> a preview reads.</summary>
internal sealed class PreviewComponentRow : ISiteOwned, ITrashable
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset? TrashedAtUtc { get; set; }
}

/// <summary>The columns of <c>pages.component_drafts</c> a preview reads.</summary>
internal sealed class PreviewComponentDraftRow : ISiteOwned
{
    public Guid ComponentId { get; set; }

    public Guid SiteId { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;
}

/// <summary>The columns of <c>pages.menus</c> a preview reads.</summary>
internal sealed class PreviewMenuRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>The columns of <c>pages.menu_drafts</c> a preview reads.</summary>
internal sealed class PreviewMenuDraftRow : ISiteOwned
{
    public Guid MenuId { get; set; }

    public Guid SiteId { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;
}

/// <summary>The columns of <c>pages.publications</c> a preview reads.</summary>
internal sealed class PreviewPublicationRow : ISiteOwned
{
    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public Guid SiteId { get; set; }

    public Guid? LiveVersionId { get; set; }
}

/// <summary>The columns of <c>pages.published_versions</c> a preview reads.</summary>
internal sealed class PreviewVersionRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public int Number { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;

    public string? Title { get; set; }

    public Guid PublishedBy { get; set; }

    public DateTimeOffset PublishedAtUtc { get; set; }
}

/// <summary>The columns of <c>pages.publish_schedules</c> a preview reads.</summary>
internal sealed class PreviewScheduleRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string SubjectKind { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset DueAtUtc { get; set; }
}
