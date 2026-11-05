namespace QuickBuilder.Identity.Domain;

/// <summary>
/// The name of someone an imported site's history mentions who is not a user of this
/// installation (020 research ST-14, data-model §2; FR-021).
/// </summary>
/// <remarks>
/// Not an account: it has no email, password, role or session, cannot sign in and is not listed
/// under Users. It exists so the author columns of imported rows (uploaded by, published by…)
/// keep pointing at a name. One row per member of the source installation, reused by later
/// imports that name the same member.
/// </remarks>
internal sealed class HistoricalAuthor
{
    public const int MaxDisplayNameLength = 200;

    private HistoricalAuthor(Guid id, Guid sourceUserId, string displayName, DateTimeOffset importedAtUtc)
    {
        Id = id;
        SourceUserId = sourceUserId;
        DisplayName = displayName;
        ImportedAtUtc = importedAtUtc;
    }

    // EF Core materialisation.
    private HistoricalAuthor()
    {
        DisplayName = string.Empty;
    }

    /// <summary>The id imported rows name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The member's id in the archive.</summary>
    public Guid SourceUserId { get; private set; }

    public string DisplayName { get; private set; }

    public DateTimeOffset ImportedAtUtc { get; private set; }

    /// <summary>
    /// A new historical author. A blank or over-long name, which only a hand-edited archive could
    /// carry, becomes a neutral one rather than refusing the import.
    /// </summary>
    public static HistoricalAuthor Create(Guid sourceUserId, string? displayName, DateTimeOffset nowUtc)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Imported member" : displayName.Trim();
        if (name.Length > MaxDisplayNameLength)
        {
            name = name[..MaxDisplayNameLength];
        }

        return new HistoricalAuthor(Guid.CreateVersion7(), sourceUserId, name, nowUtc);
    }
}
