using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure.Security;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// The single <c>installation</c> row (research R-09, R-12).
/// </summary>
/// <remarks>
/// The row is created on first need rather than only at startup, so it exists whichever
/// comes first: the startup banner, the admin shell's first status request, or a setup
/// submission. Creation is <c>INSERT … ON CONFLICT DO NOTHING</c>, so every instance ends
/// up reading — and printing — the same code.
/// </remarks>
internal sealed class InstallationStore(IdentityDbContext database, TimeProvider clock)
{
    public async Task<Installation> EnsureAsync(CancellationToken cancellationToken)
    {
        var code = Tokens.NewSetupCode();
        var now = clock.GetUtcNow();

        await database.Database
            .ExecuteSqlAsync(
                $"""
                INSERT INTO identity.installation (id, claimed_at_utc, setup_code, setup_code_created_at_utc)
                VALUES ({Installation.SingletonId}, NULL, {code}, {now})
                ON CONFLICT (id) DO NOTHING
                """,
                cancellationToken)
            .ConfigureAwait(false);

        return await database.Installation
            .AsNoTracking()
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
