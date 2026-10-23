namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>
/// The one row of <c>platform.database_version</c>: which release has migrated this database
/// (019 data-model §1, FR-027, FR-030).
/// </summary>
/// <remarks>
/// Installation-wide, not site data, so it carries no <c>SiteId</c> and no query filter. Only
/// the migration runner writes it, under the <c>platform</c> advisory lock.
/// </remarks>
internal sealed class DatabaseVersionRow
{
    /// <summary>The only id there is: a check constraint keeps the table to one row.</summary>
    internal const short SingletonId = 1;

    public short Id { get; set; } = SingletonId;

    /// <summary>
    /// The highest release that completed migrating this database. Never lowered.
    /// <c>0.0.0</c> means none has yet (a fresh or pre-release database mid-install).
    /// </summary>
    public string RecordedVersion { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>
    /// Set before the first module migration of an upgrade to a newer major.minor and cleared
    /// when every module has migrated. Non-null after a run that stopped halfway.
    /// </summary>
    public string? UpgradingTo { get; set; }

    public DateTimeOffset? UpgradingSince { get; set; }
}
