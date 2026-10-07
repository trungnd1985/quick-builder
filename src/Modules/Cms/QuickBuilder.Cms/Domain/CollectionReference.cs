using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// A reference or multi-reference field that targets another collection (data-model §1.3,
/// research CS-11).
/// </summary>
/// <remarks>
/// The target's foreign key is <c>ON DELETE RESTRICT</c>, so the database refuses to delete a
/// collection another one refers to, even against a concurrent save. Self-references are never
/// stored, so they never stand in the way of deleting a collection.
/// </remarks>
internal sealed class CollectionReference : ISiteOwned
{
    public CollectionReference(Guid siteId, Guid sourceCollectionId, string fieldId, Guid targetCollectionId)
    {
        SiteId = siteId;
        SourceCollectionId = sourceCollectionId;
        FieldId = fieldId;
        TargetCollectionId = targetCollectionId;
    }

    public Guid SiteId { get; private set; }

    public Guid SourceCollectionId { get; private set; }

    public string FieldId { get; private set; }

    public Guid TargetCollectionId { get; private set; }

    /// <summary>The rows a schema needs: one per reference field whose target is another collection.</summary>
    public static IEnumerable<CollectionReference> For(Collection collection) =>
        collection.SchemaDocument.Fields
            .Where(f => f.Target is { } target && target != collection.Id)
            .Select(f => new CollectionReference(collection.SiteId, collection.Id, f.Id, f.Target!.Value));
}
