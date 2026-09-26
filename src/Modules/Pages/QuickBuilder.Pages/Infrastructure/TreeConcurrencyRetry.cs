using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Retries a whole load–change–save of a site's page tree when another structural change
/// to the same site committed first (research P-05).
/// </summary>
/// <remarks>
/// Structural changes to one site serialise on the tree's root row. Losing that race says
/// nothing about the caller's intent — they acted on a page, not on the whole tree — so the
/// attempt is simply run again against the fresh tree, where every rule is checked anew.
/// What the caller's intent does depend on is the page's own version and the sibling they
/// placed it after, and those are checked inside the attempt, so a genuinely stale request
/// is still refused. After three lost races the caller gets <c>pages.out_of_date</c> rather
/// than looping.
/// <para>
/// The first page of a site also races: two requests may both find no tree and both insert
/// its root row. The loser's primary-key violation is the same situation — someone else
/// changed the tree first — and is retried the same way.
/// </para>
/// </remarks>
internal static class TreeConcurrencyRetry
{
    internal const int Attempts = 3;

    internal static async Task<Result<T>> ExecuteAsync<T>(
        PagesDbContext database,
        Func<CancellationToken, Task<Result<T>>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(attempt);

        for (var i = 1; ; i++)
        {
            try
            {
                return await attempt(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsLostRace(ex))
            {
                // Throw away what this attempt loaded and changed; the next one reloads.
                database.ChangeTracker.Clear();

                if (i >= Attempts)
                {
                    return Result.Failure<T>(PagesErrors.OutOfDate);
                }
            }
        }
    }

    /// <summary>
    /// Another writer got there first: the tree's version moved, or its root row now
    /// exists, or — the database's own backstop — a unique address or home page was taken
    /// between our read and our write.
    /// </summary>
    internal static bool IsLostRace(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_page_trees" or "ux_pages_site_path" or "ux_pages_site_home",
        };
}
