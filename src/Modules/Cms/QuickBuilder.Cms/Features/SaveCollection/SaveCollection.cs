using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.CreateCollection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.SaveCollection;

internal sealed record SaveCollectionCommand(Guid SiteId, Guid CollectionId, SaveCollectionRequest Request)
    : ICommand<Result<CollectionResponse>>;

/// <summary>
/// Saves a proposed definition as the collection's next revision, rewriting its items as the
/// changes require, all in one transaction (research CS-06, CS-09; FR-021, FR-022, FR-023).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Lock the collection's row (<c>FOR UPDATE NOWAIT</c>): another save, a delete or an
/// item write in progress refuses this one at once with <c>cms.collection_busy</c>.</item>
/// <item>Check the revision the proposal was based on.</item>
/// <item>Evaluate the proposal: validation, ids, targets, diff, fill-ins, impact.</item>
/// <item>When values would be lost, require the digest of the preview the member confirmed,
/// recomputed here under the lock.</item>
/// <item>Rewrite the items, store the schema, the references, the history entry and the
/// event, and commit. Any failure rolls all of it back (SC-005).</item>
/// </list>
/// </remarks>
internal sealed class SaveCollectionHandler(
    CmsDbContext database,
    CollectionLocks locks,
    ProposalEvaluator evaluator,
    ItemMigrator migrator,
    CollectionReads reads,
    CmsAccess access,
    TimeProvider clock) : ICommandHandler<SaveCollectionCommand, Result<CollectionResponse>>
{
    public async Task<Result<CollectionResponse>> HandleAsync(SaveCollectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Fail(CmsErrors.SiteNotFound);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var locked = await locks.LockForUpdateAsync(command.SiteId, command.CollectionId, cancellationToken).ConfigureAwait(false);
        if (locked.IsFailure)
        {
            return Fail(locked.Error);
        }

        if (command.Request.Revision != locked.Value)
        {
            return Fail(CmsErrors.RevisionConflict(locked.Value));
        }

        var collection = await database.ForSite<Collection>(command.SiteId)
            .SingleAsync(c => c.Id == command.CollectionId, cancellationToken)
            .ConfigureAwait(false);

        var evaluated = await evaluator.EvaluateAsync(collection, command.Request, cancellationToken).ConfigureAwait(false);
        if (evaluated.IsFailure)
        {
            return Fail(evaluated.Error);
        }

        var evaluation = evaluated.Value;
        var itemCount = evaluation.Impact.ItemCount;

        if (evaluation.Changes.Count == 0)
        {
            return Result.Success(await reads.ToResponseAsync(collection, itemCount, evaluation.Assignment.IdMap, cancellationToken).ConfigureAwait(false));
        }

        if (evaluation.Impact.RequiresConfirmation)
        {
            var preview = PreviewResponse.From(collection.Revision, evaluation.Impact);
            if (command.Request.Confirmation?.Digest is not { } digest)
            {
                return Fail(CmsErrors.ConfirmationRequired(preview));
            }

            if (!string.Equals(digest, preview.Digest, StringComparison.Ordinal))
            {
                return Fail(CmsErrors.PreviewOutdated(preview));
            }
        }

        await migrator.MigrateAsync(collection, evaluation.Plans, cancellationToken).ConfigureAwait(false);

        var actor = access.Actor;
        var entry = collection.Apply(evaluation.Details, evaluation.Assignment, evaluation.Impact, actor, clock);
        database.SchemaChanges.Add(entry);

        var stale = await database.ForSite<CollectionReference>(command.SiteId)
            .Where(r => r.SourceCollectionId == collection.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        database.CollectionReferences.RemoveRange(stale);
        database.CollectionReferences.AddRange(CollectionReference.For(collection));

        CmsOutbox.AddFrom(database, collection, actor);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (CreateCollectionHandler.IsSlugTaken(ex))
        {
            return Fail(CmsErrors.SlugTaken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // A target collection was deleted between the check and this commit (research CS-11).
            return Fail(CmsErrors.UnknownTargetCollection("/fields"));
        }

        return Result.Success(await reads.ToResponseAsync(collection, itemCount, evaluation.Assignment.IdMap, cancellationToken).ConfigureAwait(false));
    }

    private static Result<CollectionResponse> Fail(Error error) => Result.Failure<CollectionResponse>(error);
}

/// <summary>Administrative surface: save a collection's details and schema.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}")]
public sealed class SaveCollectionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(CmsCapabilities.CollectionsManage)]
    [RequestSizeLimit(262_144)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, Guid collectionId, SaveCollectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new SaveCollectionCommand(siteId, collectionId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
