using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// The Sites module's data context. Owns the <c>sites</c> schema and nothing else
/// (FR-022).
/// </summary>
internal sealed class SitesDbContext(DbContextOptions<SitesDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "sites";

    internal DbSet<Site> Sites => Set<Site>();

    internal DbSet<SiteHostName> HostNames => Set<SiteHostName>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>
    /// The site global query filters compare against (FR-013).
    /// </summary>
    /// <remarks>
    /// This must be a property <em>on the context</em>, and the filter expression must
    /// reference it through <c>this</c>. EF Core builds and caches the model once per
    /// context type, so a filter closing over an injected <c>ISiteContext</c> captures
    /// whichever instance happened to build the model — in practice the one from startup
    /// migration, where no site is resolved. Every later request then filters against a
    /// stale empty site and matches nothing.
    /// <para>
    /// Referencing a context property instead makes EF lift it into a query parameter that
    /// is re-evaluated per query, against the context actually running it.
    /// </para>
    /// <para>
    /// <see cref="Guid.Empty"/> when no site is resolved, which fails closed: an
    /// administrative request sees no site-owned rows rather than all of them.
    /// </para>
    /// </remarks>
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // citext: host names are case-insensitive by definition. Putting that in the
        // column beats relying on every caller to lowercase correctly.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<Site>(entity =>
        {
            entity.ToTable("sites", Schema);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(Site.MaxNameLength).IsRequired();

            entity
                .HasMany(e => e.HostNames)
                .WithOne()
                .HasForeignKey(h => h.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.HostNames).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SiteHostName>(entity =>
        {
            entity.ToTable("site_host_names", Schema);
            entity.HasKey(e => e.Id);

            entity
                .Property(e => e.HostName)
                .HasColumnType("citext")
                .HasMaxLength(SiteHostName.MaxLength)
                .IsRequired();

            // Global uniqueness, not per site: resolution is host name -> site, so two
            // sites claiming one host name would be ambiguous. See SiteHostName remarks.
            entity
                .HasIndex(e => e.HostName)
                .IsUnique()
                .HasDatabaseName("ux_site_host_names_host_name");

            entity.HasIndex(e => e.SiteId).HasDatabaseName("ix_site_host_names_site_id");

            // FR-013. Expressed here rather than through a generic helper because the
            // filter has to reference CurrentSiteId on this context — see its remarks for
            // why a captured ISiteContext silently filters against the wrong site.
            entity.HasQueryFilter(h => h.SiteId == CurrentSiteId);
        });

        modelBuilder.AddOutbox(Schema);

        // Before the index filters below are turned into SQL: they name columns such as
        // completed_at_utc, so the naming pass has to have run first.
        modelBuilder.UseSnakeCaseColumns();

        // Site filters are declared per entity above (see CurrentSiteId).

        base.OnModelCreating(modelBuilder);
    }
}
