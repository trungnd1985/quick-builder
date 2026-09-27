using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// The Pages module's data context. Owns the <c>pages</c> schema and nothing else (001 FR-022).
/// </summary>
/// <remarks>
/// Every entity is site-owned and filtered against <see cref="CurrentSiteId"/>. Every Pages
/// endpoint is on the administrative surface, where no site is resolved and the filters
/// therefore match nothing, so handlers read through <see cref="ForSite{T}"/> — the
/// module's one cross-site read (research P-15).
/// </remarks>
internal sealed class PagesDbContext(DbContextOptions<PagesDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "pages";

    internal DbSet<PageTree> PageTrees => Set<PageTree>();

    internal DbSet<Page> Pages => Set<Page>();

    internal DbSet<PageDraft> PageDrafts => Set<PageDraft>();

    internal DbSet<Component> Components => Set<Component>();

    internal DbSet<ComponentDraft> ComponentDrafts => Set<ComponentDraft>();

    internal DbSet<ComponentUsage> ComponentUsages => Set<ComponentUsage>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>
    /// The site the query filters compare against. A property on the context, referenced
    /// through <c>this</c>, for the reason <c>SiteQueryFilterGuidance</c> documents.
    /// <see cref="Guid.Empty"/> when no site is resolved, which fails closed.
    /// </summary>
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    /// <summary>
    /// The rows of <typeparamref name="T"/> that belong to <paramref name="siteId"/>, on the
    /// administrative surface (research P-15).
    /// </summary>
    /// <remarks>
    /// The module's only <c>AcrossAllSites</c> call. The admin surface has no resolved site,
    /// so the query filters match nothing there; every Pages endpoint names its site in the
    /// route instead, and has already checked the caller may see it. Keeping the bypass and
    /// its <c>SiteId</c> predicate together here means no handler can drop one and keep the
    /// other, and there is one reason to review rather than seventeen.
    /// <c>PagesSiteScopingTests</c> proves it returns only the named site's rows, and that no
    /// other call site exists.
    /// </remarks>
    internal IQueryable<T> ForSite<T>(Guid siteId)
        where T : class, ISiteOwned =>
        Set<T>()
            .AcrossAllSites("Admin operation on a site named explicitly in the route (001 FR-011b).")
            .Where(e => e.SiteId == siteId);

    /// <summary>
    /// Makes the next save update the tree's root row, so its <c>xmin</c> is checked and
    /// structural changes to one site serialise (research P-05).
    /// </summary>
    /// <remarks>
    /// A change such as a rename touches only <c>pages</c> rows unless the root row changes
    /// too, and <see cref="PageTree.UpdatedAtUtc"/> alone may not move when two changes land
    /// in one clock tick. Marking it modified forces the update either way, as
    /// <c>SitesDbContext.MarkAggregateChanged</c> does for sites.
    /// </remarks>
    internal void MarkTreeChanged(PageTree tree) =>
        Entry(tree).Property(t => t.UpdatedAtUtc).IsModified = true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // citext: component names are unique ignoring case (FR-024). Sites already installs
        // the extension; IF NOT EXISTS makes a second request harmless.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<PageTree>(entity =>
        {
            entity.ToTable("page_trees", Schema, t =>
                t.HasCheckConstraint("ck_page_trees_page_count", $"page_count BETWEEN 0 AND {PageAddresses.MaxPagesPerSite}"));

            entity.HasKey(e => e.SiteId);
            entity.Property(e => e.SiteId).ValueGeneratedNever();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            entity.Ignore(e => e.DomainEvents);

            entity
                .HasMany(e => e.Pages)
                .WithOne()
                .HasForeignKey(p => p.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Pages).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<Page>(entity =>
        {
            entity.ToTable("pages", Schema, t =>
            {
                t.HasCheckConstraint("ck_pages_position", "position >= 0");
                t.HasCheckConstraint("ck_pages_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                t.HasCheckConstraint("ck_pages_depth", $"depth BETWEEN 1 AND {PageAddresses.MaxDepth}");
                t.HasCheckConstraint("ck_pages_home_top_level", "NOT is_home OR (parent_id IS NULL AND path = '/')");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            // For component_usages' composite foreign key: a usage names a page of its own site.
            entity.HasAlternateKey(e => new { e.SiteId, e.Id });

            entity.Property(e => e.Title).HasMaxLength(Page.MaxTitleLength).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(PageAddresses.MaxSlugLength).IsRequired();
            entity.Property(e => e.Path).HasMaxLength(PageAddresses.MaxPathLength).IsRequired();
            entity.Property(e => e.Depth).HasColumnType("smallint");
            entity.Property(e => e.IsHome).HasDefaultValue(false);

            // A page with children cannot be deleted (clarification Q2): the domain refuses it,
            // and this refuses it again should anything ever get past.
            entity
                .HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // FR-006: the backstop behind the tree's own check, and rendering's lookup in #12.
            entity.HasIndex(e => new { e.SiteId, e.Path }).IsUnique().HasDatabaseName("ux_pages_site_path");

            // FR-003: at most one home page per site.
            entity
                .HasIndex(e => e.SiteId)
                .IsUnique()
                .HasFilter("is_home")
                .HasDatabaseName("ux_pages_site_home");

            // Tree load order. Not unique: renumbering rewrites a sibling group row by row (P-06).
            entity
                .HasIndex(e => new { e.SiteId, e.ParentId, e.Position })
                .HasDatabaseName("ix_pages_site_parent_position");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PageDraft>(entity =>
        {
            entity.ToTable("page_drafts", Schema, t =>
                t.HasCheckConstraint("ck_page_drafts_schema_version", "schema_version = 1"));

            entity.HasKey(e => e.PageId);
            entity.Property(e => e.PageId).ValueGeneratedNever();
            ConfigureDraft(entity);

            entity
                .HasOne<Page>()
                .WithOne()
                .HasForeignKey<PageDraft>(e => e.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<Component>(entity =>
        {
            entity.ToTable("components", Schema);

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasAlternateKey(e => new { e.SiteId, e.Id });

            entity.Property(e => e.Name).HasColumnType("citext").HasMaxLength(Component.MaxNameLength).IsRequired();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            entity.Ignore(e => e.DomainEvents);

            entity
                .HasIndex(e => new { e.SiteId, e.Name })
                .IsUnique()
                .HasDatabaseName("ux_components_site_name");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<ComponentDraft>(entity =>
        {
            entity.ToTable("component_drafts", Schema, t =>
                t.HasCheckConstraint("ck_component_drafts_schema_version", "schema_version = 1"));

            entity.HasKey(e => e.ComponentId);
            entity.Property(e => e.ComponentId).ValueGeneratedNever();
            ConfigureDraft(entity);

            entity
                .HasOne<Component>()
                .WithOne()
                .HasForeignKey<ComponentDraft>(e => e.ComponentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<ComponentUsage>(entity =>
        {
            entity.ToTable("component_usages", Schema);
            entity.HasKey(e => new { e.PageId, e.ComponentId });

            // P-11: both keys include the site, so a usage can only join a page and a
            // component of the same site. Deleting the page takes its usages with it;
            // deleting a component that is still used is refused.
            entity
                .HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => new { e.SiteId, e.PageId })
                .HasPrincipalKey(p => new { p.SiteId, p.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne<Component>()
                .WithMany()
                .HasForeignKey(e => new { e.SiteId, e.ComponentId })
                .HasPrincipalKey(c => new { c.SiteId, c.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.SiteId, e.ComponentId }).HasDatabaseName("ix_component_usages_site_component");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // Every module context carries the outbox (003 analysis U1).
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureDraft<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : Draft
    {
        entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
        entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
        entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        entity.Ignore(e => e.DomainEvents);
    }
}
