using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Trash;

/// <summary>
/// The purge date of a trash entry, computed when it is read (021 research TR-15).
/// </summary>
/// <remarks>
/// Nothing stores a purge date, so a changed <see cref="TrashOptions.RetentionDays"/> applies
/// to every entry already in the trash at once (FR-025).
/// </remarks>
public sealed class TrashRetention(IOptionsMonitor<TrashOptions> options)
{
    /// <summary>The current retention period, in days.</summary>
    public int Days => options.CurrentValue.RetentionDays;

    /// <summary>When an entry trashed at <paramref name="trashedAtUtc"/> is purged.</summary>
    public DateTimeOffset PurgeOn(DateTimeOffset trashedAtUtc) => trashedAtUtc.AddDays(Days);

    /// <summary>Entries trashed at or before this moment are due for purging at <paramref name="now"/>.</summary>
    public DateTimeOffset DueBefore(DateTimeOffset now) => now.AddDays(-Days);
}
