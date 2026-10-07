using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Media.Infrastructure;

/// <summary><see cref="IMediaContentTypes"/> over the Media context, for the site the caller names.</summary>
internal sealed class MediaContentTypes(MediaDbContext database) : IMediaContentTypes
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);

        if (mediaIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await database.MediaFiles
            .AcrossAllSites("Admin read of a named site's files for another module's admin work (014 research CS-12, 001 FR-011b).")
            .AsNoTracking()
            .Where(f => f.SiteId == siteId && mediaIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.ContentType, cancellationToken)
            .ConfigureAwait(false);
    }
}
