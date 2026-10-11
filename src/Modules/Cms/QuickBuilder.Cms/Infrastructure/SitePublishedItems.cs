using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The public path's published-item reader, for one named site, on the administrative surface
/// (016 research TB-14): the editor's list preview shows what visitors would see.
/// </summary>
/// <remarks>
/// The administrative surface resolves no site, and only the resolution middleware may set
/// one, so the request's site context cannot be used. Instead the read-only context is built
/// with a site of its own: the same read-only connection, the same published-only mapping,
/// and its query filters on exactly the site the request names.
/// </remarks>
internal sealed class SitePublishedItems(DbContextOptions<CmsPublishedReadDbContext> options)
{
    /// <summary>A reader over <paramref name="siteId"/>'s published items; dispose it after use.</summary>
    public Scoped For(Guid siteId)
    {
        var database = new CmsPublishedReadDbContext(options, new FixedSite(siteId));
        return new Scoped(database, new PublishedItemReader(database));
    }

    /// <summary>A reader and the context it owns.</summary>
    internal sealed class Scoped(CmsPublishedReadDbContext database, PublishedItemReader reader) : IAsyncDisposable
    {
        public PublishedItemReader Reader => reader;

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class FixedSite(Guid siteId) : ISiteContext
    {
        public Guid? SiteId => siteId;

        public bool HasSite => true;
    }
}
