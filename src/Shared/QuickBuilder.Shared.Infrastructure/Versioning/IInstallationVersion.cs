namespace QuickBuilder.Shared.Infrastructure.Versioning;

/// <summary>The running release and the one the database records (019 FR-006, installation-version.md §2).</summary>
/// <param name="Version">This build, e.g. <c>0.2.0</c>.</param>
/// <param name="Revision">The first 12 characters of its git revision, when known.</param>
/// <param name="DatabaseVersion">The highest release that completed migrating the database, or
/// null when none has yet.</param>
/// <param name="UpgradingTo">An upgrade in progress, or interrupted, when there is one.</param>
public sealed record InstallationVersionInfo(SemanticVersion Version, string? Revision, string? DatabaseVersion, string? UpgradingTo);

/// <summary>Reads the version record for anyone who may show it (Identity, to Administrators).</summary>
public interface IInstallationVersion
{
    Task<InstallationVersionInfo> GetAsync(CancellationToken cancellationToken = default);
}
