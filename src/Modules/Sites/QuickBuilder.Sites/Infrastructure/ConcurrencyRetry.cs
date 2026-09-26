using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Retries a whole load–change–save of the site aggregate when another change to the same
/// site committed first (009 research S-05).
/// </summary>
/// <remarks>
/// For actions whose caller sends no version — adding or removing a host name, disabling
/// or enabling — losing a race says nothing about the caller's intent, so the action is
/// simply tried again against the fresh state, where the aggregate re-checks its rules.
/// That is what stops two concurrent changes from, say, removing the primary host name.
/// After three lost races the caller gets <c>sites.out_of_date</c> rather than looping.
/// <para>
/// Settings changes do not use this: their caller's version is the point, and a stale one
/// is refused (research S-06).
/// </para>
/// </remarks>
internal static class ConcurrencyRetry
{
    internal const int Attempts = 3;

    internal static async Task<Result<T>> ExecuteAsync<T>(
        SitesDbContext database,
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
            catch (DbUpdateConcurrencyException) when (i < Attempts)
            {
                // Throw away what this attempt loaded and changed; the next one reloads.
                database.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                database.ChangeTracker.Clear();
                return Result.Failure<T>(OutOfDate());
            }
        }
    }

    internal static Error OutOfDate() => Error.Conflict(
        "sites.out_of_date",
        "The site was changed by someone else at the same time. Reload it and try again.");
}
