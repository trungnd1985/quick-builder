namespace QuickBuilder.Identity.Contracts;

/// <summary>A member an archive names: its id there and its display name (020 research ST-14).</summary>
public sealed record ArchivedAuthor(Guid SourceUserId, string? DisplayName);

/// <summary>
/// Maps the members an imported archive names to ids on this installation (020 FR-021, research
/// ST-14).
/// </summary>
/// <remarks>
/// A member who is a user here keeps their id: always so for a restore, and for an archive of
/// this installation. Anyone else becomes a historical author, a name with no account, created
/// once and reused by later imports that name the same member. No account, role or invitation
/// is ever created.
/// </remarks>
public interface IHistoricalAuthors
{
    /// <summary>The id on this installation of every author given, keyed by their id in the archive.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> EnsureAsync(IReadOnlyList<ArchivedAuthor> authors, CancellationToken cancellationToken = default);
}
