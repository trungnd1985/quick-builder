using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// <see cref="IPublishedDesignReader"/> over a read-only context (012 research PR-06, PR-17).
/// </summary>
internal sealed class PublishedDesignReader(PublishedDesignReadDbContext database, DesignTokenUpgrades upgrades) : IPublishedDesignReader
{
    private static readonly Func<PublishedDesignReadDbContext, Guid, CancellationToken, Task<int>> LiveNumber =
        EF.CompileAsyncQuery((PublishedDesignReadDbContext db, Guid siteId, CancellationToken _) =>
            db.Publications
                .Where(p => p.SiteId == siteId)
                .Select(p => p.LiveNumber)
                .FirstOrDefault());

    public Task<int> GetLiveNumberAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        LiveNumber(database, siteId, cancellationToken);

    public async Task<PublishedDesign> GetLiveAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var live = await (
                from publication in database.Publications
                where publication.SiteId == siteId
                join version in database.Versions
                    on new { publication.SiteId, Number = publication.LiveNumber } equals new { version.SiteId, version.Number }
                select version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (live is null)
        {
            return new PublishedDesign(0, DesignTokens.CurrentVersion, DefaultDesignTokens.Document);
        }

        // 019 RL-19: at the current schema version. One that cannot be used falls back to the
        // defaults, as a site with no published design does, rather than failing every page.
        return upgrades.ForRendering(siteId, live.Document, live.SchemaVersion) is { } current
            ? new PublishedDesign(live.Number, DesignTokens.CurrentVersion, current)
            : new PublishedDesign(live.Number, DesignTokens.CurrentVersion, DefaultDesignTokens.Document);
    }
}

/// <summary>
/// The public path's view of the published design: read-only (012 research PR-06).
/// </summary>
/// <remarks>
/// On a connection PostgreSQL keeps read-only, with <c>SaveChanges</c> refused as well. Maps
/// tables <see cref="EditorDbContext"/> owns and migrates; this context is never migrated.
/// </remarks>
internal sealed class PublishedDesignReadDbContext(DbContextOptions<PublishedDesignReadDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal DbSet<DesignPublicationRow> Publications => Set<DesignPublicationRow>();

    internal DbSet<DesignVersionRow> Versions => Set<DesignVersionRow>();

    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    public override int SaveChanges() => throw ReadOnly();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(EditorDbContext.Schema);

        modelBuilder.Entity<DesignPublicationRow>(entity =>
        {
            entity.ToTable("design_publications");
            entity.HasKey(e => e.SiteId);
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.Entity<DesignVersionRow>(entity =>
        {
            entity.ToTable("design_versions");
            entity.HasKey(e => new { e.SiteId, e.Number });
            entity.Property(e => e.SchemaVersion).HasColumnType("smallint");
            entity.Property(e => e.Document).HasColumnType("jsonb");
            entity.HasQueryFilter(QueryFilters.Site, e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static InvalidOperationException ReadOnly() =>
        new("The published-design context is read-only (constitution Principle VII). Write through EditorDbContext.");
}

/// <summary>The columns of <c>editor.design_publications</c> the public path reads.</summary>
internal sealed class DesignPublicationRow : ISiteOwned
{
    public Guid SiteId { get; set; }

    public int LiveNumber { get; set; }
}

/// <summary>The columns of <c>editor.design_versions</c> the public path reads.</summary>
internal sealed class DesignVersionRow : ISiteOwned
{
    public Guid SiteId { get; set; }

    public int Number { get; set; }

    public int SchemaVersion { get; set; }

    public string Document { get; set; } = string.Empty;
}
