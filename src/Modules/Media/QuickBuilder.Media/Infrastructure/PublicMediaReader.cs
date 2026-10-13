using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
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
/// The variant list comes from the row and the configuration alone (013 MV-15): a variant
/// not produced yet is produced when the browser first asks for it.
/// </remarks>
internal sealed class PublicMediaReader(
    PublicMediaReadDbContext database,
    IOptions<MediaOptions> options,
    ImagingRuntime imaging) : IPublicMediaReader
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
            .Select(f => new { f.Id, f.Extension, f.ContentType, f.ContentVersion, f.Width, f.Height, f.IsAnimated, f.MetadataState, f.VariantsState, f.AltText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var widths = options.Value.Variants.Widths;

        // The same address MediaFileMapper.PublicPath builds, without the base path.
        return files.ToDictionary(
            f => f.Id,
            f =>
            {
                var eligible = VariantPlan.IsEligible(f.ContentType, f.IsAnimated, f.MetadataState, f.VariantsState, imaging.AvifVariants);
                IReadOnlyList<PublicMediaVariant> variants = eligible
                    ? [.. VariantPlan.Widths(f.Width, widths).Select(w => new PublicMediaVariant(w, new MediaAddress(f.Id, f.ContentVersion, w, f.Extension).ToPath()))]
                    : [];

                return new PublicMediaFile(
                    new MediaAddress(f.Id, f.ContentVersion, null, f.Extension).ToPath(),
                    f.ContentType,
                    f.Width,
                    f.Height,
                    variants,
                    OfferOriginal: eligible && VariantPlan.OfferOriginal(f.Width, widths),
                    AltText: f.AltText);
            });
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
            entity.Property(e => e.MetadataState).HasConversion<short>();
            entity.Property(e => e.VariantsState).HasConversion<short>();
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

    public int ContentVersion { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public bool IsAnimated { get; set; }

    public MediaMetadataState MetadataState { get; set; }

    public MediaVariantsState VariantsState { get; set; }

    /// <summary>The file's alternative text, for <c>og:image:alt</c> (017 research SE-05).</summary>
    public string? AltText { get; set; }
}
