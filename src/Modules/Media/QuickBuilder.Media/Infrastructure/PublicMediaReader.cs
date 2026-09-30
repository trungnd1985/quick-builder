using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// <see cref="IPublicMediaReader"/> over a read-only context (012 research PR-06, PR-11).
/// </summary>
/// <remarks>
/// A deleted file's row is gone (003's delete is permanent), so "the row exists" is what
/// "available" means here. Checking the stored bytes on every render would put file-system
/// calls on the public path for a case — bytes removed by hand — the admin already reports.
/// </remarks>
internal sealed class PublicMediaReader(PublicMediaReadDbContext database) : IPublicMediaReader
{
    public async Task<IReadOnlyDictionary<Guid, PublicMediaFile>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);

        if (mediaIds.Count == 0)
        {
            return new Dictionary<Guid, PublicMediaFile>();
        }

        var files = await database.Files
            .Where(f => f.SiteId == siteId && mediaIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Extension, f.ContentType })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The same address MediaFileMapper.PublicPath builds, without the base path.
        return files.ToDictionary(f => f.Id, f => new PublicMediaFile($"/_media/{f.Id:N}.{f.Extension}", f.ContentType));
    }
}

/// <summary>
/// The public path's view of the media library: read-only (012 research PR-06).
/// </summary>
/// <remarks>
/// On a connection PostgreSQL keeps read-only, with <c>SaveChanges</c> refused as well. Maps
/// the table <see cref="MediaDbContext"/> owns and migrates; this context is never migrated.
/// </remarks>
internal sealed class PublicMediaReadDbContext(DbContextOptions<PublicMediaReadDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal DbSet<PublicMediaRow> Files => Set<PublicMediaRow>();

    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    public override int SaveChanges() => throw ReadOnly();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(MediaDbContext.Schema);

        modelBuilder.Entity<PublicMediaRow>(entity =>
        {
            entity.ToTable("media_files");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => e.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }

    private static InvalidOperationException ReadOnly() =>
        new("The public media context is read-only (constitution Principle VII). Write through MediaDbContext.");
}

/// <summary>The columns of <c>media.media_files</c> the public path reads.</summary>
internal sealed class PublicMediaRow : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Extension { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
}
