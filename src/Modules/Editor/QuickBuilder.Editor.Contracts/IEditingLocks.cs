namespace QuickBuilder.Editor.Contracts;

/// <summary>The documents editing sessions are about (023 research CE-01).</summary>
public static class EditingDocumentKinds
{
    public const string Page = "page";

    public const string Component = "component";

    public const string Item = "item";

    public static bool IsKnown(string? kind) => kind is Page or Component or Item;
}

/// <summary>The header the admin sends on draft saves (023 research CE-07).</summary>
public static class EditingHeaders
{
    public const string Session = "X-QB-Editing-Session";

    /// <summary>The header's value as a session id; null when absent or not a guid.</summary>
    public static Guid? Parse(string? value) => Guid.TryParse(value, out var id) ? id : null;
}

/// <summary>
/// Whether a draft write from an editing session may proceed (023 research CE-07, FR-018,
/// FR-026).
/// </summary>
/// <remarks>
/// The lock is soft: the version check of the owning module is what protects every draft.
/// This only closes the race where a session that lost the lock (a takeover, a move) still
/// has a save on its way. It is a read made before the caller's own transaction, so no
/// transaction spans two modules; a takeover between the check and the write is harmless,
/// because the new holder loaded the draft before it and their next save is refused by
/// version.
/// </remarks>
public interface IEditingLocks
{
    /// <summary>
    /// Allowed when <paramref name="sessionId"/> is null, when no live session holds the
    /// document's lock, or when <paramref name="sessionId"/> is the holder; refused otherwise.
    /// One indexed read. An unknown session is simply not the holder.
    /// </summary>
    Task<EditingWriteCheck> CheckWriteAsync(
        Guid siteId,
        string documentKind,
        Guid documentId,
        Guid? sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>The answer of <see cref="IEditingLocks.CheckWriteAsync"/>.</summary>
/// <param name="Allowed">Whether the write may go on to the version check.</param>
/// <param name="HolderUserId">When refused: who holds the lock.</param>
/// <param name="HolderSince">When refused: since when they hold it.</param>
public sealed record EditingWriteCheck(bool Allowed, Guid? HolderUserId, DateTimeOffset? HolderSince)
{
    public static EditingWriteCheck Yes { get; } = new(true, null, null);

    public static EditingWriteCheck HeldBy(Guid userId, DateTimeOffset since) => new(false, userId, since);
}
