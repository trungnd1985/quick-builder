using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>What an item write may rely on once it holds the gate: the schema in force until it commits.</summary>
internal sealed record GateEntry(int Revision, CollectionSchemaDocument Schema);

/// <summary>
/// The one way into writing a collection's items (research CS-10, FR-037, data-model §3.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every item write — save, publish, unpublish or delete, from feature 015 on — must enter
/// here first</b>, in its own transaction, and validate its values against the schema this
/// returns. It takes <c>FOR SHARE NOWAIT</c> on the collection's row: while a schema change
/// holds the row, the write is refused at once with <c>cms.items_being_updated</c>; while the
/// write holds it, a schema change is refused with <c>cms.collection_busy</c>. So an item can
/// never be written in a shape the schema has just left, and readers see either the data
/// before a change or after it, never a mix.
/// </para>
/// <para>
/// No item writer exists in this feature; <c>ItemWriteGateTests</c> proves the gate.
/// </para>
/// </remarks>
internal sealed class CollectionWriteGate(CmsDbContext database, CollectionLocks locks)
{
    public async Task<Result<GateEntry>> EnterAsync(Guid siteId, Guid collectionId, int? expectedRevision, CancellationToken cancellationToken)
    {
        var locked = await locks.LockForShareAsync(siteId, collectionId, CmsErrors.ItemsBeingUpdated, cancellationToken).ConfigureAwait(false);
        if (locked.IsFailure)
        {
            return Result.Failure<GateEntry>(locked.Error);
        }

        if (expectedRevision is { } expected && expected != locked.Value)
        {
            return Result.Failure<GateEntry>(CmsErrors.RevisionConflict(locked.Value));
        }

        var schema = await database.ForSite<Collection>(siteId)
            .AsNoTracking()
            .Where(c => c.Id == collectionId)
            .Select(c => c.Schema)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new GateEntry(locked.Value, CollectionSchemaDocument.Read(schema)));
    }
}
