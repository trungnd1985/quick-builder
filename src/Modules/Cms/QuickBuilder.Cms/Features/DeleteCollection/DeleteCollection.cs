using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.DeleteCollection;

internal sealed record DeleteCollectionCommand(Guid SiteId, Guid CollectionId, int Revision) : ICommand<Result>;

/// <summary>A collection and field that refer to one being deleted.</summary>
public sealed record ReferenceResponse(Guid CollectionId, string CollectionName, string FieldId, string FieldName);

/// <summary>
/// Deletes a collection with its schema, history and every item, draft and published,
/// permanently (FR-004, clarification Q5, research CS-11).
/// </summary>
/// <remarks>
/// Refused while a reference field of another collection targets it. The check names them;
/// the <c>RESTRICT</c> foreign key decides a race with a save that adds such a field.
/// </remarks>
internal sealed class DeleteCollectionHandler(
    CmsDbContext database,
    CollectionLocks locks,
    CmsAccess access,
    ICollectionUsageReader pageUses,
    TimeProvider clock) : ICommandHandler<DeleteCollectionCommand, Result>
{
    /// <summary>How many of the pages and components using a collection the refusal names.</summary>
    public const int ListedUses = 50;

    public async Task<Result> HandleAsync(DeleteCollectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(CmsErrors.SiteNotFound);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var locked = await locks.LockForUpdateAsync(command.SiteId, command.CollectionId, cancellationToken).ConfigureAwait(false);
        if (locked.IsFailure)
        {
            return Result.Failure(locked.Error);
        }

        if (command.Revision != locked.Value)
        {
            return Result.Failure(CmsErrors.RevisionConflict(locked.Value));
        }

        if (await ReferencesAsync(command.SiteId, command.CollectionId, cancellationToken).ConfigureAwait(false) is { Count: > 0 } references)
        {
            return Result.Failure(CmsErrors.CollectionReferenced(references));
        }

        // 016 FR-010: refused while a page or component uses it, in a draft, live or scheduled.
        // Checked after 014's reference refusal, which is returned first and unchanged. A draft
        // saved in between is refused by page-schema rule 14 at its next save (TB-13).
        var used = await pageUses.ListUsesAsync(command.SiteId, command.CollectionId, ListedUses, cancellationToken).ConfigureAwait(false);
        if (used.Total > 0)
        {
            return Result.Failure(CmsErrors.CollectionUsedByPages(
                [.. used.Uses.Select(u => (object)new { kind = u.Kind, id = u.Id, title = u.Title, states = u.States })], used.Total));
        }

        var collection = await database.ForSite<Collection>(command.SiteId)
            .SingleAsync(c => c.Id == command.CollectionId, cancellationToken)
            .ConfigureAwait(false);

        // 015 research IT-10: references between this collection's own items go first. The
        // cascade would remove them too, but PostgreSQL checks item_references' NO ACTION key on
        // the cascaded item delete before it cascades to those rows. No other collection's item
        // can refer to these: the collection_references check above refuses that.
        var items = database.ForSite<Item>(command.SiteId).Where(i => i.CollectionId == collection.Id).Select(i => i.Id);
        await database.ForSite<ItemReference>(command.SiteId)
            .Where(r => items.Contains(r.SourceItemId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        collection.MarkDeleted(clock);
        CmsOutbox.AddFrom(database, collection, access.Actor);
        database.Collections.Remove(collection);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Result.Failure(CmsErrors.CollectionReferenced([]));
        }

        return Result.Success();
    }

    private async Task<IReadOnlyList<object>> ReferencesAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken)
    {
        var rows = await database.ForSite<CollectionReference>(siteId)
            .Where(r => r.TargetCollectionId == collectionId)
            .Join(
                database.ForSite<Collection>(siteId),
                r => r.SourceCollectionId,
                c => c.Id,
                (r, c) => new { r.SourceCollectionId, c.Name, c.Schema, r.FieldId })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.FieldId, StringComparer.Ordinal)
            .Select(r => (object)new ReferenceResponse(
                r.SourceCollectionId,
                r.Name,
                r.FieldId,
                CollectionSchemaDocument.Read(r.Schema).Find(r.FieldId)?.Name ?? r.FieldId))];
    }
}

/// <summary>Administrative surface: delete a collection.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}")]
public sealed class DeleteCollectionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpDelete]
    [RequireCapability(CmsCapabilities.CollectionsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid collectionId, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteCollectionCommand(siteId, collectionId, revision), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error!) : NoContent();
    }
}
