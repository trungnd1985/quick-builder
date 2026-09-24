using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// Keeps at least one active administrator, even under concurrent changes (spec FR-013;
/// research R-13).
/// </summary>
/// <remarks>
/// Every change that could remove an administrator — deactivation, demotion, replacing
/// grants — takes one transaction-scoped PostgreSQL advisory lock first, then recounts.
/// Two administrators demoting each other at the same moment therefore run one after the
/// other, and the second sees that it would leave nobody. The lock ends with the
/// transaction, even if the process dies.
/// </remarks>
internal sealed class AdministratorGuard(IdentityDbContext database)
{
    /// <summary>"IDADMINS" in ASCII: a fixed key only this guard uses.</summary>
    private const long LockKey = 0x4944_4144_4D49_4E53;

    /// <summary>Serialises administrator-affecting changes. Requires an open transaction.</summary>
    public Task LockAsync(CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The administrator lock is transaction-scoped; open a transaction first.");
        }

        return database.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey})", cancellationToken);
    }

    /// <summary>
    /// True when an active administrator other than <paramref name="excludingUserId"/>
    /// exists. Call after <see cref="LockAsync"/>, inside the same transaction.
    /// </summary>
    public Task<bool> AnotherActiveAdministratorExistsAsync(Guid excludingUserId, CancellationToken cancellationToken) =>
        database.RoleGrants
            .Where(g => g.Role == SystemRole.Administrator && g.UserId != excludingUserId)
            .Join(database.Users, g => g.UserId, u => u.Id, (g, u) => u)
            .AnyAsync(u => u.Status == UserStatus.Active, cancellationToken);

    public Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken) =>
        database.RoleGrants.AnyAsync(g => g.UserId == userId && g.Role == SystemRole.Administrator, cancellationToken);
}
