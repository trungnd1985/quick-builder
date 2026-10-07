using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Whether a slug is free in a collection, and the next free one (015 research IT-03).
/// </summary>
/// <remarks>
/// Advisory: the admin asks as the member types, and a refused save is answered with a
/// suggestion. Only the <c>item_slugs</c> key decides, at the save. The item being edited may
/// keep its own slugs.
/// </remarks>
internal sealed class SlugAvailability(CmsDbContext database)
{
    private const int CandidatesPerQuery = 20;

    public async Task<bool> IsFreeAsync(Guid siteId, Guid collectionId, string slug, Guid? itemId, CancellationToken cancellationToken) =>
        !await Taken(siteId, collectionId, itemId)
            .AnyAsync(s => s.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

    /// <summary><paramref name="slug"/> when free, otherwise the first free of <c>-2</c>, <c>-3</c>, …</summary>
    public Task<string> NextFreeAsync(Guid siteId, Guid collectionId, string slug, Guid? itemId, CancellationToken cancellationToken) =>
        FirstFreeAsync(siteId, collectionId, SlugRules.Candidates(slug), itemId, cancellationToken);

    /// <summary>The first slug of <paramref name="candidates"/> no other item holds.</summary>
    public async Task<string> FirstFreeAsync(Guid siteId, Guid collectionId, IEnumerable<string> candidates, Guid? itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var chunk in candidates.Chunk(CandidatesPerQuery))
        {
            var names = chunk.ToList();
            var taken = await Taken(siteId, collectionId, itemId)
                .Where(s => names.Contains(s.Slug))
                .Select(s => s.Slug)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var free = names.FirstOrDefault(n => !taken.Contains(n, StringComparer.OrdinalIgnoreCase));
            if (free is not null)
            {
                return free;
            }
        }

        throw new InvalidOperationException("The candidate slugs never end, so one is always free.");
    }

    private IQueryable<ItemSlug> Taken(Guid siteId, Guid collectionId, Guid? itemId) =>
        database.ForSite<ItemSlug>(siteId)
            .AsNoTracking()
            .Where(s => s.CollectionId == collectionId && (itemId == null || s.ItemId != itemId));
}
