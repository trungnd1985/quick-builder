using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;

namespace QuickBuilder.Shared.Infrastructure.MultiSite;

/// <summary>
/// The one permitted way to read across sites (FR-013a).
/// </summary>
/// <remarks>
/// Both <c>IgnoreQueryFilters</c> overloads are banned project-wide by BannedSymbols.txt
/// with RS0030 escalated to an error, so this file and <c>TrashQuery</c> hold the only
/// suppressions in the codebase. Any
/// other attempt to escape the site filter fails the build at compile time rather than
/// being something a reviewer has to notice.
/// <para>
/// Every caller must supply a written reason. That reason is not decoration: it is what a
/// reviewer reads to decide whether the bypass is legitimate, and an architecture test
/// asserts each call site has one.
/// </para>
/// <para>
/// Legitimate uses are administrative and few — listing every site on the admin surface,
/// resolving a host name to a site before any site is known. If a new call site is not
/// obviously one of those, it is probably a bug.
/// </para>
/// </remarks>
public static class CrossSiteQuery
{
    /// <summary>
    /// Reads across every site, bypassing the SiteId filter.
    /// </summary>
    /// <remarks>
    /// Lifts the site filter only; trashed rows stay hidden (021 TR-03). To see trashed rows
    /// as well, combine with <c>TrashQuery.IncludingTrash</c> or <c>OnlyTrash</c>.
    /// </remarks>
    /// <param name="source">The query to unfilter.</param>
    /// <param name="reason">
    /// Why this read legitimately spans sites. Required, non-empty, and written for a
    /// reviewer rather than for a machine.
    /// </param>
    public static IQueryable<T> AcrossAllSites<T>(this IQueryable<T> source, string reason)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        // The single permitted use in the codebase. See the class remarks.
#pragma warning disable RS0030 // Do not use banned APIs
        return source.IgnoreQueryFilters([QueryFilters.Site]);
#pragma warning restore RS0030
    }
}
