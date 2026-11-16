using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Shared.Infrastructure.Trash;

/// <summary>
/// The only ways to see rows that are in the trash (021 TR-03).
/// </summary>
/// <remarks>
/// Every <see cref="ITrashable"/> entity carries the named filter
/// <see cref="QueryFilters.Trash"/>, so trashed rows are hidden from every read without a
/// handler having to remember (FR-006). These helpers lift that filter, and only that
/// filter: the site filter stays, so a trash read is still one site's read unless it is
/// also <c>AcrossAllSites</c>.
/// <para>
/// Callers are few and listed in <c>TrashQueryUsageTests</c>: the trash slices, the purge
/// services, the media orphan sweep and other background readers that must count trashed
/// rows as existing, the usage readers, and the transfer archive parts. Every caller gives
/// a written reason, as <c>CrossSiteQuery</c> requires.
/// </para>
/// </remarks>
public static class TrashQuery
{
    /// <summary>Reads live and trashed rows alike.</summary>
    /// <param name="source">The query.</param>
    /// <param name="reason">Why this read must see trashed rows, written for a reviewer.</param>
    public static IQueryable<T> IncludingTrash<T>(this IQueryable<T> source, string reason)
        where T : class, ITrashable
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        // One of the two permitted uses in the codebase; see CrossSiteQuery.
#pragma warning disable RS0030 // Do not use banned APIs
        return source.IgnoreQueryFilters([QueryFilters.Trash]);
#pragma warning restore RS0030
    }

    /// <summary>Reads trashed rows only.</summary>
    /// <param name="source">The query.</param>
    /// <param name="reason">Why this read must see trashed rows, written for a reviewer.</param>
    public static IQueryable<T> OnlyTrash<T>(this IQueryable<T> source, string reason)
        where T : class, ITrashable =>
        source.IncludingTrash(reason).Where(e => e.TrashedAtUtc != null);
}
