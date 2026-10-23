using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>
/// <see cref="IInstallationVersion"/> over the <c>platform</c> record. Lives beside the runner:
/// only the migrations namespace touches <see cref="PlatformDbContext"/> (an architecture test).
/// </summary>
internal sealed class InstallationVersion(PlatformDbContext database, ApplicationVersion version) : IInstallationVersion
{
    public async Task<InstallationVersionInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await database.DatabaseVersion.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var recorded = row is null || row.RecordedVersion == SemanticVersion.Zero.ToString() ? null : row.RecordedVersion;
        return new InstallationVersionInfo(version.Version, version.Revision, recorded, row?.UpgradingTo);
    }
}
