using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Publishing a site's design, and rolling it back (012 FR-010, research PR-17).
/// </summary>
/// <remarks>
/// Each action is one transaction: the version, the live pointer, the retention prune and the
/// <see cref="DesignPublished"/> event land together. Two publishes of one site's design at once
/// serialise on the publication's <c>xmin</c>, as page publishes do (PR-03). Every published
/// page takes the new design at its next request, because its cache key carries the live design
/// number (PR-05).
/// </remarks>
internal sealed class DesignPublishing(
    EditorDbContext database,
    IDesignTokenReader tokens,
    EditorAccess access,
    TimeProvider clock,
    ILogger<DesignPublishing> logger)
{
    private const int Attempts = 8;

    /// <summary>Publishes the draft token set the caller saw (<paramref name="version"/>, 0 for the defaults).</summary>
    public Task<Result<DesignPublication>> PublishAsync(Guid siteId, uint version, CancellationToken cancellationToken) =>
        RunAsync(siteId, async (publication, now) =>
        {
            var draft = await tokens.ReadAsync(siteId, cancellationToken).ConfigureAwait(false);
            if (draft.Version != version)
            {
                return Result.Failure<DesignVersion>(EditorErrors.OutOfDate);
            }

            // Stored sets were validated when saved, and the defaults are the contract's own
            // golden file: nothing unchecked is ever published.
            return Result.Success(publication.Publish(draft.Document.GetRawText(), DesignTokens.CurrentVersion, draft.Version, access.Actor, now));
        }, cancellationToken);

    /// <summary>Publishes a retained design version again; the draft token set is not changed.</summary>
    public Task<Result<DesignPublication>> RollbackAsync(Guid siteId, int versionNumber, CancellationToken cancellationToken) =>
        RunAsync(siteId, async (publication, now) =>
        {
            var source = await database.ForSite<DesignVersion>(siteId)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Number == versionNumber, cancellationToken)
                .ConfigureAwait(false);

            return source is null
                ? Result.Failure<DesignVersion>(EditorErrors.DesignVersionNotFound)
                : Result.Success(publication.Rollback(source, access.Actor, now));
        }, cancellationToken);

    private async Task<Result<DesignPublication>> RunAsync(
        Guid siteId,
        Func<DesignPublication, DateTimeOffset, Task<Result<DesignVersion>>> attempt,
        CancellationToken cancellationToken)
    {
        if (!await access.SiteExistsAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DesignPublication>(EditorErrors.SiteNotFound);
        }

        for (var i = 1; ; i++)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var publication = await database.ForSite<DesignPublication>(siteId)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (publication is null)
                {
                    publication = DesignPublication.Start(siteId);
                    database.DesignPublications.Add(publication);
                }

                var now = clock.GetUtcNow();
                var version = await attempt(publication, now).ConfigureAwait(false);
                if (version.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    database.ChangeTracker.Clear();
                    return Result.Failure<DesignPublication>(version.Error);
                }

                database.DesignVersions.Add(version.Value);
                database.OutboxMessages.Add(OutboxMessage.From(new DesignPublished(
                    Guid.CreateVersion7(),
                    now,
                    siteId,
                    version.Value.Number,
                    version.Value.Kind,
                    version.Value.RestoredFromNumber,
                    access.Actor)));

                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // PR-15: the 50 newest versions stay; the live one always does.
                var oldest = version.Value.Number - DesignPublication.RetainedVersions;
                if (oldest > 0)
                {
                    await database.ForSite<DesignVersion>(siteId)
                        .Where(v => v.Number <= oldest)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();

                // Numbers only: token names and values are content (FR-044).
                logger.LogInformation(
                    "The design of site {SiteId} is live as version {VersionNumber} ({VersionKind}).",
                    siteId, version.Value.Number, version.Value.Kind);
                return Result.Success(publication);
            }
            catch (DbUpdateException ex) when (IsLostRace(ex))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();

                if (i >= Attempts)
                {
                    return Result.Failure<DesignPublication>(EditorErrors.OutOfDate);
                }

#pragma warning disable CA5394 // Jitter, not security.
                await Task.Delay(Random.Shared.Next(5, 25 * i), cancellationToken).ConfigureAwait(false);
#pragma warning restore CA5394
            }
        }
    }

    private static bool IsLostRace(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_design_publications" or "pk_design_versions",
        };
}
