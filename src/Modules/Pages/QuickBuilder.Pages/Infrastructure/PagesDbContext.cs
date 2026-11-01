using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Domain.Redirects;
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

    internal DbSet<Publication> Publications => Set<Publication>();

    internal DbSet<PublishedVersion> PublishedVersions => Set<PublishedVersion>();

    internal DbSet<LiveDependency> LiveDependencies => Set<LiveDependency>();

    internal DbSet<DraftMediaReference> DraftMediaReferences => Set<DraftMediaReference>();

    internal DbSet<VersionMediaReference> VersionMediaReferences => Set<VersionMediaReference>();

    internal DbSet<PublishSchedule> PublishSchedules => Set<PublishSchedule>();

    internal DbSet<PublishLogEntry> PublishLog => Set<PublishLogEntry>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    internal DbSet<CollectionUsage> CollectionUsages => Set<CollectionUsage>();

    internal DbSet<Redirect> Redirects => Set<Redirect>();

    internal DbSet<RedirectUsage> RedirectUsages => Set<RedirectUsage>();

    internal DbSet<RedirectChange> RedirectChanges => Set<RedirectChange>();

    internal DbSet<SitemapState> SitemapStates => Set<SitemapState>();

    internal DbSet<Menu> Menus => Set<Menu>();

    internal DbSet<MenuDraft> MenuDrafts => Set<MenuDraft>();

    internal DbSet<MenuTarget> MenuTargets => Set<MenuTarget>();

    internal DbSet<MenuUsage> MenuUsages => Set<MenuUsage>();

    internal DbSet<MenuChange> MenuChanges => Set<MenuChange>();

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
                // 023 data-model §2.1: a copy's origin is all there or not at all.
                t.HasCheckConstraint("ck_pages_copy_origin", "(copied_from_id IS NULL) = (copied_from_saved_at_utc IS NULL) AND (copied_from_id IS NULL) = (copied_by IS NULL) AND (copied_from_id IS NULL) = (copied_at_utc IS NULL)");
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

            // 016 FR-002: a collection has at most one template. One template per parent needs
            // no index of its own: the template's path is its parent's address and /{slug},
            // which ux_pages_site_path keeps unique (research TB-02).
            entity
                .HasIndex(e => new { e.SiteId, e.TemplateCollectionId })
                .IsUnique()
                .HasFilter("template_collection_id IS NOT NULL")
                .HasDatabaseName("ux_pages_site_template_collection");

            entity.Ignore(e => e.IsTemplate);
            entity.Ignore(e => e.CopyOrigin);

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
            entity.ToTable("components", Schema, t =>
                // 023 data-model §2.1.
                t.HasCheckConstraint("ck_components_copy_origin", "(copied_from_id IS NULL) = (copied_from_saved_at_utc IS NULL) AND (copied_from_id IS NULL) = (copied_by IS NULL) AND (copied_from_id IS NULL) = (copied_at_utc IS NULL)"));

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasAlternateKey(e => new { e.SiteId, e.Id });

            entity.Property(e => e.Name).HasColumnType("citext").HasMaxLength(Component.MaxNameLength).IsRequired();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.CopyOrigin);

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

        ConfigurePublishing(modelBuilder);

        ConfigureMenus(modelBuilder);

        // Every module context carries the outbox (003 analysis U1).
        modelBuilder.AddOutbox(Schema);

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Publishing (012 data-model §1).</summary>
    /// <remarks>
    /// A publication's subject is a page or a component, so it has no single foreign key to
    /// it; the page and component deletes remove publications, versions and schedules
    /// explicitly, in their own transaction (research PR-15). The log has no foreign key at
    /// all: it outlives its subjects (FR-017).
    /// </remarks>
    private void ConfigurePublishing(ModelBuilder modelBuilder)
    {
        const string subjectKinds = "subject_kind IN ('page', 'component')";

        // 024 research NV-01: menus are published like components. Media and collection uses
        // keep the narrower list: a menu shows neither.
        const string publishedKinds = "subject_kind IN ('page', 'component', 'menu')";

        modelBuilder.Entity<Publication>(entity =>
        {
            entity.ToTable("publications", Schema, t =>
            {
                t.HasCheckConstraint("ck_publications_subject_kind", publishedKinds);
                t.HasCheckConstraint("ck_publications_live", "(live_version_id IS NULL) = (live_number IS NULL)");
            });

            entity.HasKey(e => new { e.SubjectKind, e.SubjectId });
            entity.Property(e => e.SubjectKind).HasMaxLength(16);
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            entity.Ignore(e => e.DomainEvents);
            entity.Ignore(e => e.IsLive);
            entity.Property(e => e.PageParamNames).HasDefaultValueSql("'{}'::text[]");

            entity
                .HasIndex(e => e.SiteId)
                .HasFilter("live_version_id IS NOT NULL")
                .HasDatabaseName("ix_publications_site_live");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PublishedVersion>(entity =>
        {
            entity.ToTable("published_versions", Schema, t =>
            {
                t.HasCheckConstraint("ck_published_versions_subject_kind", publishedKinds);
                t.HasCheckConstraint("ck_published_versions_kind", "kind IN ('publish', 'scheduled', 'rollback')");
                t.HasCheckConstraint("ck_published_versions_number", "number >= 1");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.SubjectKind).HasMaxLength(16);
            entity.Property(e => e.Kind).HasMaxLength(16);
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Title).HasMaxLength(Page.MaxTitleLength);

            entity
                .HasOne<Publication>()
                .WithMany()
                .HasForeignKey(e => new { e.SubjectKind, e.SubjectId })
                .OnDelete(DeleteBehavior.Cascade);

            // PR-03: a repeated number is impossible even if the retry logic were wrong.
            entity
                .HasIndex(e => new { e.SubjectKind, e.SubjectId, e.Number })
                .IsUnique()
                .HasDatabaseName("ux_published_versions_subject_number");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<LiveDependency>(entity =>
        {
            entity.ToTable("live_dependencies", Schema, t =>
            {
                t.HasCheckConstraint("ck_live_dependencies_subject_kind", publishedKinds);
                // 016 data-model §1.2: list sources and a template's own collection. 024
                // data-model §3: Navigation blocks' menus, and menu entries' items.
                t.HasCheckConstraint("ck_live_dependencies_target_kind", "target_kind IN ('component', 'page', 'media', 'collection', 'template', 'menu', 'item')");
            });

            entity.HasKey(e => new { e.SiteId, e.SubjectKind, e.SubjectId, e.TargetKind, e.TargetId });
            entity.Property(e => e.SubjectKind).HasMaxLength(16);
            entity.Property(e => e.TargetKind).HasMaxLength(16);

            // PR-04: "who depends on this?" is the question every refresh asks.
            entity
                .HasIndex(e => new { e.SiteId, e.TargetKind, e.TargetId })
                .HasDatabaseName("ix_live_dependencies_target");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // 013 data-model.md §2.1, §2.2 (research MV-05): where media files are used.
        modelBuilder.Entity<DraftMediaReference>(entity =>
        {
            entity.ToTable("draft_media_references", Schema, t =>
                t.HasCheckConstraint("ck_draft_media_references_subject_kind", subjectKinds));

            entity.HasKey(e => new { e.SubjectKind, e.SubjectId, e.MediaId });
            entity.Property(e => e.SubjectKind).HasMaxLength(16);

            // "Where is this file used?" is the question Media asks.
            entity.HasIndex(e => new { e.SiteId, e.MediaId }).HasDatabaseName("ix_draft_media_references_media");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<VersionMediaReference>(entity =>
        {
            entity.ToTable("version_media_references", Schema);

            entity.HasKey(e => new { e.VersionId, e.MediaId });

            entity
                .HasOne<PublishedVersion>()
                .WithMany()
                .HasForeignKey(e => e.VersionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.SiteId, e.MediaId }).HasDatabaseName("ix_version_media_references_media");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        // 016 data-model §1.3 (research TB-13): where collections are used.
        modelBuilder.Entity<CollectionUsage>(entity =>
        {
            entity.ToTable("collection_uses", Schema, t =>
            {
                t.HasCheckConstraint("ck_collection_uses_subject_kind", subjectKinds);
                t.HasCheckConstraint("ck_collection_uses_state", "state IN ('draft', 'live', 'scheduled')");
            });

            entity.HasKey(e => new { e.SiteId, e.SubjectKind, e.SubjectId, e.State, e.CollectionId });
            entity.Property(e => e.SubjectKind).HasMaxLength(16);
            entity.Property(e => e.State).HasMaxLength(16);

            // "Who uses this collection?" is the question Cms asks before a delete.
            entity.HasIndex(e => new { e.SiteId, e.CollectionId }).HasDatabaseName("ix_collection_uses_collection");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<PublishSchedule>(entity =>
        {
            entity.ToTable("publish_schedules", Schema, t =>
            {
                t.HasCheckConstraint("ck_publish_schedules_subject_kind", publishedKinds);
                t.HasCheckConstraint("ck_publish_schedules_action", "action IN ('publish', 'unpublish')");
                t.HasCheckConstraint("ck_publish_schedules_state", "state IN ('pending', 'done', 'cancelled', 'failed')");
                t.HasCheckConstraint("ck_publish_schedules_unpublish_page", "action = 'publish' OR subject_kind = 'page'");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.SubjectKind).HasMaxLength(16);
            entity.Property(e => e.Action).HasMaxLength(16);
            entity.Property(e => e.State).HasMaxLength(16);
            entity.Property(e => e.CapturedDocument).HasColumnType("jsonb");
            entity.Property(e => e.CapturedSchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.CapturedTitle).HasMaxLength(Page.MaxTitleLength);
            entity.Property(e => e.FailureCode).HasMaxLength(100);
            entity.Ignore(e => e.IsPending);

            // 017 FR-032: no foreign key, as for a link target; a deleted page simply gets no
            // redirect when the schedule runs.
            entity.Property(e => e.RedirectToPageId);

            // FR-018: at most one pending schedule per subject and action.
            entity
                .HasIndex(e => new { e.SubjectKind, e.SubjectId, e.Action })
                .IsUnique()
                .HasFilter("state = 'pending'")
                .HasDatabaseName("ux_publish_schedules_pending");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        ConfigureRedirects(modelBuilder);

        modelBuilder.Entity<PublishLogEntry>(entity =>
        {
            entity.ToTable("publish_log", Schema, t =>
                t.HasCheckConstraint("ck_publish_log_subject_kind", "subject_kind IN ('page', 'component', 'menu', 'design')"));

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.SubjectKind).HasMaxLength(16);
            entity.Property(e => e.SubjectLabel).HasMaxLength(Page.MaxTitleLength).IsRequired();
            entity.Property(e => e.SubjectPath).HasMaxLength(PageAddresses.MaxPathLength);
            entity.Property(e => e.Action).HasMaxLength(32);
            entity.Property(e => e.ReasonCode).HasMaxLength(100);

            entity
                .HasIndex(e => new { e.SiteId, e.OccurredAtUtc, e.Id })
                .IsDescending(false, true, true)
                .HasDatabaseName("ix_publish_log_site_time");

            entity
                .HasIndex(e => new { e.SiteId, e.SubjectKind, e.SubjectId, e.OccurredAtUtc })
                .IsDescending(false, false, false, true)
                .HasDatabaseName("ix_publish_log_subject");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });
    }

    /// <summary>017 data-model §1.1–§1.3 and §1.7: redirects, their usage and audit, and the sitemap generation.</summary>
    private void ConfigureRedirects(ModelBuilder modelBuilder)
    {
        static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

        modelBuilder.Entity<Redirect>(entity =>
        {
            entity.ToTable("redirects", Schema, t =>
            {
                t.HasCheckConstraint("ck_redirects_match_kind", $"match_kind IN ({InList(RedirectMatchKinds.All)})");
                t.HasCheckConstraint("ck_redirects_status_code", "status_code IN (301, 302)");
                t.HasCheckConstraint("ck_redirects_origin", $"origin IN ({InList(RedirectOrigins.All)})");
                t.HasCheckConstraint(
                    "ck_redirects_target",
                    "(target_kind = 'page' AND target_page_id IS NOT NULL AND target_item_id IS NULL AND target_path IS NULL AND target_url IS NULL)"
                    + " OR (target_kind = 'item' AND target_collection_id IS NOT NULL AND target_item_id IS NOT NULL AND target_page_id IS NULL AND target_path IS NULL AND target_url IS NULL)"
                    + " OR (target_kind = 'path' AND target_path IS NOT NULL AND target_page_id IS NULL AND target_item_id IS NULL AND target_url IS NULL)"
                    + " OR (target_kind = 'url' AND target_url IS NOT NULL AND target_page_id IS NULL AND target_item_id IS NULL AND target_path IS NULL)");

                // FR-017a, FR-033: only exact sources take a query; prefixes send to a page or a path.
                t.HasCheckConstraint(
                    "ck_redirects_prefix",
                    "match_kind = 'exact' OR (source_query = '' AND target_kind IN ('page', 'path'))");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.MatchKind).HasMaxLength(16);
            entity.Property(e => e.SourcePath).HasMaxLength(PageAddresses.MaxPathLength);
            entity.Property(e => e.SourceQuery).HasMaxLength(512);
            entity.Property(e => e.StatusCode).HasColumnType("smallint");
            entity.Property(e => e.TargetKind).HasMaxLength(16);
            entity.Property(e => e.TargetPath).HasMaxLength(1600);
            entity.Property(e => e.TargetUrl).HasMaxLength(2048);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.Origin).HasMaxLength(16);
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            // FR-017: one redirect per source. Also the index the public lookups use (SE-10).
            entity
                .HasIndex(e => new { e.SiteId, e.MatchKind, e.SourcePath, e.SourceQuery })
                .IsUnique()
                .HasDatabaseName("ux_redirects_source");

            entity
                .HasIndex(e => new { e.SiteId, e.TargetPageId })
                .HasFilter("target_page_id IS NOT NULL")
                .HasDatabaseName("ix_redirects_target_page");

            entity
                .HasIndex(e => new { e.SiteId, e.TargetPath })
                .HasFilter("target_path IS NOT NULL")
                .HasDatabaseName("ix_redirects_target_path");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<RedirectUsage>(entity =>
        {
            entity.ToTable("redirect_usage", Schema);
            entity.HasKey(e => e.RedirectId);
            entity.Property(e => e.RedirectId).ValueGeneratedNever();
            entity
                .HasOne<Redirect>()
                .WithOne()
                .HasForeignKey<RedirectUsage>(e => e.RedirectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<RedirectChange>(entity =>
        {
            entity.ToTable("redirect_changes", Schema, t =>
            {
                t.HasCheckConstraint("ck_redirect_changes_action", $"action IN ({InList(RedirectChangeActions.All)})");
                t.HasCheckConstraint("ck_redirect_changes_origin", $"origin IN ({InList(RedirectOrigins.All)})");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Action).HasMaxLength(16);
            entity.Property(e => e.Origin).HasMaxLength(16);

            // No foreign key: the record outlives the redirect (FR-021).
            entity
                .HasIndex(e => new { e.SiteId, e.RedirectId, e.AtUtc })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_redirect_changes_redirect");

            entity
                .HasIndex(e => new { e.SiteId, e.AtUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_redirect_changes_site_time");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<SitemapState>(entity =>
        {
            entity.ToTable("sitemap_state", Schema);
            entity.HasKey(e => e.SiteId);
            entity.Property(e => e.SiteId).ValueGeneratedNever();
            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });
    }

    /// <summary>024 data-model §1.1–§1.5: menus, their drafts, their indexes and their audit.</summary>
    private void ConfigureMenus(ModelBuilder modelBuilder)
    {
        static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

        modelBuilder.Entity<Menu>(entity =>
        {
            entity.ToTable("menus", Schema);

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasAlternateKey(e => new { e.SiteId, e.Id });

            entity.Property(e => e.Name).HasColumnType("citext").HasMaxLength(Menu.MaxNameLength).IsRequired();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            // FR-002: names are unique per site, ignoring letter case.
            entity
                .HasIndex(e => new { e.SiteId, e.Name })
                .IsUnique()
                .HasDatabaseName("ux_menus_site_name");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<MenuDraft>(entity =>
        {
            entity.ToTable("menu_drafts", Schema, t =>
                t.HasCheckConstraint("ck_menu_drafts_schema_version", "schema_version = 1"));

            entity.HasKey(e => e.MenuId);
            entity.Property(e => e.MenuId).ValueGeneratedNever();
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            entity
                .HasOne<Menu>()
                .WithOne()
                .HasForeignKey<MenuDraft>(e => e.MenuId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<MenuTarget>(entity =>
        {
            entity.ToTable("menu_targets", Schema, t =>
                t.HasCheckConstraint("ck_menu_targets_target_kind", "target_kind IN ('page', 'item')"));

            entity.HasKey(e => new { e.SiteId, e.MenuId, e.TargetKind, e.TargetId });
            entity.Property(e => e.TargetKind).HasMaxLength(16);

            // Both keys include the site: a target row belongs to a menu of its own site.
            entity
                .HasOne<Menu>()
                .WithMany()
                .HasForeignKey(e => new { e.SiteId, e.MenuId })
                .HasPrincipalKey(m => new { m.SiteId, m.Id })
                .OnDelete(DeleteBehavior.Cascade);

            // FR-026: "which menus point at this page or item?"
            entity
                .HasIndex(e => new { e.SiteId, e.TargetKind, e.TargetId })
                .HasDatabaseName("ix_menu_targets_target");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<MenuUsage>(entity =>
        {
            entity.ToTable("menu_usages", Schema, t =>
                t.HasCheckConstraint("ck_menu_usages_subject_kind", "subject_kind IN ('page', 'component')"));

            entity.HasKey(e => new { e.SiteId, e.SubjectKind, e.SubjectId, e.MenuId });
            entity.Property(e => e.SubjectKind).HasMaxLength(16);

            // NV-06: no restricting key. Deleting a menu that only drafts show is allowed, and
            // takes these rows with it.
            entity
                .HasOne<Menu>()
                .WithMany()
                .HasForeignKey(e => new { e.SiteId, e.MenuId })
                .HasPrincipalKey(m => new { m.SiteId, m.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.SiteId, e.MenuId }).HasDatabaseName("ix_menu_usages_menu");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<MenuChange>(entity =>
        {
            entity.ToTable("menu_changes", Schema, t =>
                t.HasCheckConstraint("ck_menu_changes_action", $"action IN ({InList(MenuChangeActions.All)})"));

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Action).HasMaxLength(16);

            // No foreign key: the record outlives the menu (FR-005).
            entity
                .HasIndex(e => new { e.SiteId, e.MenuId, e.AtUtc })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_menu_changes_menu");

            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });
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
