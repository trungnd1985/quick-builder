using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Media.Variants;

/// <summary>
/// Which instance produces a variant, decided in the database (013 research MV-12,
/// data-model.md §1.4).
/// </summary>
/// <remarks>
/// Inserting the claim row wins. A unique violation means another instance is producing it;
/// a claim older than the lease belongs to an instance that died, and is taken over with a
/// conditional update that only one caller can win. Each call uses its own short-lived scope,
/// so no connection is held while pixels are produced.
/// </remarks>
internal sealed class VariantClaims(IServiceScopeFactory scopes, TimeProvider clock)
{
    /// <summary>Random per process: whose claim a row is, for diagnostics.</summary>
    internal static readonly Guid InstanceId = Guid.NewGuid();

    /// <summary>Tries to claim the production of one variant. True when this instance should produce it.</summary>
    public async Task<bool> TryClaimAsync(VariantKey key, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var now = clock.GetUtcNow();

        database.VariantClaims.Add(new VariantClaim(key.SiteId, key.FileId, key.Version, key.Width, now, InstanceId));
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone holds it. Take it over only when their lease has run out.
            var stale = now - lease;
            var taken = await Claims(database, key)
                .Where(c => c.ClaimedAtUtc < stale)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.ClaimedAtUtc, now).SetProperty(c => c.InstanceId, InstanceId),
                    cancellationToken)
                .ConfigureAwait(false);

            return taken == 1;
        }
    }

    /// <summary>Gives the claim up, produced or not.</summary>
    public async Task ReleaseAsync(VariantKey key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        await Claims(database, key)
            .Where(c => c.InstanceId == InstanceId)
            .ExecuteDeleteAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static IQueryable<VariantClaim> Claims(MediaDbContext database, VariantKey key) =>
        database.VariantClaims
            .AcrossAllSites("Variant claims are keyed by file, version and width; the site is checked explicitly (013 MV-12).")
            .Where(c => c.SiteId == key.SiteId && c.FileId == key.FileId && c.ContentVersion == key.Version && c.Width == key.Width);
}
