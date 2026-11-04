using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Identity.Domain;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>Identity's side of <see cref="IHistoricalAuthors"/> (020 research ST-14).</summary>
/// <remarks>
/// Runs in Identity's own transaction, just before an import applies: the rows are names only,
/// and one left by an import that later fails is reused by the next import of the same member,
/// so it does not need to join the import's transaction.
/// </remarks>
internal sealed class HistoricalAuthors(IdentityDbContext database, TimeProvider clock) : IHistoricalAuthors
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> EnsureAsync(IReadOnlyList<ArchivedAuthor> authors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authors);

        var map = new Dictionary<Guid, Guid>();
        var ids = authors.Select(a => a.SourceUserId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return map;
        }

        foreach (var batch in ids.Chunk(1000))
        {
            foreach (var user in await database.Users.AsNoTracking().Where(u => batch.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                map[user] = user;
            }

            foreach (var known in await database.HistoricalAuthors.AsNoTracking()
                         .Where(h => batch.Contains(h.SourceUserId))
                         .Select(h => new { h.SourceUserId, h.Id })
                         .ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                map.TryAdd(known.SourceUserId, known.Id);
            }
        }

        var now = clock.GetUtcNow();
        foreach (var author in authors.DistinctBy(a => a.SourceUserId).Where(a => !map.ContainsKey(a.SourceUserId)))
        {
            var created = HistoricalAuthor.Create(author.SourceUserId, author.DisplayName, now);
            database.HistoricalAuthors.Add(created);
            map[author.SourceUserId] = created.Id;
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two imports naming the same member raced; read the winner's rows.
            database.ChangeTracker.Clear();
            return await EnsureAsync(authors, cancellationToken).ConfigureAwait(false);
        }

        return map;
    }
}
