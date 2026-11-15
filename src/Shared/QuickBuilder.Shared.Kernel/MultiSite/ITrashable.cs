namespace QuickBuilder.Shared.Kernel.MultiSite;

/// <summary>
/// An entity that can be in its site's trash (021 TR-02).
/// </summary>
/// <remarks>
/// A trashed row is the content row itself, marked with <see cref="TrashedAtUtc"/>. Every
/// <c>ITrashable</c> entity carries the named query filter <c>QueryFilters.Trash</c>, so a
/// trashed row is hidden from every read without a handler writing the comparison; only
/// <c>TrashQuery.IncludingTrash</c> and <c>TrashQuery.OnlyTrash</c> can see it.
/// <c>QueryFilterCoverageTests</c> asserts the filter is present.
/// </remarks>
public interface ITrashable
{
    /// <summary>When the entity was moved to the trash; null while it is live.</summary>
    DateTimeOffset? TrashedAtUtc { get; }
}
