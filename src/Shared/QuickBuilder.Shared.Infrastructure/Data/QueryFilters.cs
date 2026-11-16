namespace QuickBuilder.Shared.Infrastructure.Data;

/// <summary>
/// The names of the EF Core query filters every module declares (021 TR-03).
/// </summary>
/// <remarks>
/// EF Core 10 allows several named filters per entity and lifting them one by one, but not
/// an anonymous filter next to a named one, so every filter in the codebase is named.
/// <list type="bullet">
/// <item><see cref="Site"/>: on every <c>ISiteOwned</c> entity, comparing its site with the
/// context's current site. Lifted only by <c>CrossSiteQuery.AcrossAllSites</c>.</item>
/// <item><see cref="Trash"/>: on every <c>ITrashable</c> entity, <c>TrashedAtUtc == null</c>.
/// Lifted only by <c>TrashQuery.IncludingTrash</c> and <c>TrashQuery.OnlyTrash</c>.</item>
/// </list>
/// </remarks>
public static class QueryFilters
{
    /// <summary>The site filter.</summary>
    public const string Site = "site";

    /// <summary>The trash filter.</summary>
    public const string Trash = "trash";
}
