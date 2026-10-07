using System.Text.Json;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Domain.Events;
using QuickBuilder.UnitTests.Sites;
using static QuickBuilder.UnitTests.Cms.SchemaTestKit;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// The collection aggregate: revisions, permanent ids that are never reused, the title field
/// and its events (data-model §3.1, research CS-02, FR-006, FR-011).
/// </summary>
public sealed class CollectionTests
{
    private static readonly Guid Site = Guid.Parse("0192f5a0-0000-7000-8000-00000000000a");
    private static readonly Guid Actor = Guid.Parse("0192f5a0-0000-7000-8000-00000000000b");
    private readonly SteppedClock _clock = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));

    private Collection NewCollection() => Collection.Create(Site, Details, Actor, _clock);

    /// <summary>Applies a proposal the way the save handler does: normalise, assign ids, diff, store.</summary>
    private SchemaChangeEntry Save(Collection collection, CollectionSchemaDocument proposal, CollectionDetails? details = null)
    {
        var assignment = collection.AssignIds(proposal.Normalise());
        var changes = SchemaDiff.Compare(
            collection.Details, collection.SchemaDocument, details ?? collection.Details, assignment.Document,
            assignment.IdMap.Values.ToHashSet(StringComparer.Ordinal));
        var impact = new ImpactSummary([.. changes.Select(c => new ChangeImpact(c, FieldConversion.Classify(c)))], 0);
        return collection.Apply(details ?? collection.Details, assignment, impact, Actor, _clock);
    }

    [Fact]
    public void A_new_collection_is_empty_at_revision_one_and_says_so()
    {
        var collection = NewCollection();

        Assert.Equal((1, 1, 1), (collection.Revision, collection.NextFieldNumber, collection.NextOptionNumber));
        Assert.Empty(collection.SchemaDocument.Fields);
        var created = Assert.IsType<CollectionCreatedEvent>(Assert.Single(collection.DomainEvents));
        Assert.Equal((Site, collection.Id, 1), (created.SiteId, created.CollectionId, created.Revision));
    }

    [Fact]
    public void Temporary_ids_get_the_next_permanent_ids_and_the_counters_advance()
    {
        var collection = NewCollection();

        Save(collection, Doc(Text("~1"), Of(FieldType.Option, "~2", "kind") with { Options = [new("~3", "A"), new("~4", "B")] }));

        Assert.Equal(["f1", "f2"], collection.SchemaDocument.Fields.Select(f => f.Id));
        Assert.Equal(["o1", "o2"], collection.SchemaDocument.Fields[1].Options.Select(o => o.Id));
        Assert.Equal((3, 3, 2), (collection.NextFieldNumber, collection.NextOptionNumber, collection.Revision));
    }

    [Fact]
    public void A_removed_fields_id_is_never_issued_again()
    {
        var collection = NewCollection();
        Save(collection, Doc(Text("~1"), Of(FieldType.Boolean, "~2", "featured")));
        Save(collection, Doc(collection.SchemaDocument.Fields[0]));

        Save(collection, Doc(collection.SchemaDocument.Fields[0], Of(FieldType.Boolean, "~1", "featured")));

        Assert.Equal(["f1", "f3"], collection.SchemaDocument.Fields.Select(f => f.Id));
    }

    [Fact]
    public void The_first_single_line_text_field_names_the_items_and_becomes_required()
    {
        var collection = NewCollection();

        Save(collection, Doc(Of(FieldType.Number, "~1", "price"), Text("~2", "headline")));

        Assert.Equal("f2", collection.SchemaDocument.TitleFieldId);
        Assert.True(collection.SchemaDocument.Find("f2")!.Required);
    }

    [Fact]
    public void A_schema_with_no_single_line_text_field_has_no_title_field()
    {
        var collection = NewCollection();

        Save(collection, Doc(Text("~1", "body", multiline: true)));

        Assert.Null(collection.SchemaDocument.TitleFieldId);
    }

    [Fact]
    public void Each_save_is_one_revision_with_a_change_event_and_a_history_entry()
    {
        var collection = NewCollection();
        collection.ClearDomainEvents();

        var entry = Save(collection, Doc(Text("~1"), Of(FieldType.Boolean, "~2", "featured")));

        var changed = Assert.IsType<CollectionChangedEvent>(Assert.Single(collection.DomainEvents));
        Assert.Equal((2, true, false), (changed.Revision, changed.SchemaChanged, changed.ItemsRewritten));
        Assert.Equal((collection.Id, 2), (entry.CollectionId, entry.Revision));

        using var summary = JsonDocument.Parse(entry.Summary);
        Assert.Equal(["fieldAdded", "fieldAdded", "titleFieldChanged"], summary.RootElement.EnumerateArray().Select(e => e.GetProperty("kind").GetString()));
    }

    [Fact]
    public void A_details_only_save_does_not_change_the_schema()
    {
        var collection = NewCollection();
        collection.ClearDomainEvents();

        Save(collection, collection.SchemaDocument, Details with { Name = "Articles", Slug = "articles" });

        var changed = Assert.IsType<CollectionChangedEvent>(Assert.Single(collection.DomainEvents));
        Assert.False(changed.SchemaChanged);
        Assert.Equal(("Articles", "articles"), (collection.Name, collection.Slug));
    }

    [Fact]
    public void A_save_with_nothing_to_change_is_refused_as_a_revision()
    {
        var collection = NewCollection();

        Assert.Throws<InvalidOperationException>(() => Save(collection, collection.SchemaDocument));
        Assert.Equal(1, collection.Revision);
    }

    [Fact]
    public void References_to_other_collections_are_kept_and_self_references_are_not()
    {
        var collection = NewCollection();
        Save(collection, Doc(
            Of(FieldType.Reference, "~1", "author"),
            Of(FieldType.MultiReference, "~2", "related")));

        // Point the second field at the collection itself.
        var fields = collection.SchemaDocument.Fields;
        Save(collection, Doc(fields[0], fields[1] with { Target = collection.Id }));

        var reference = Assert.Single(CollectionReference.For(collection));
        Assert.Equal(("f1", People), (reference.FieldId, reference.TargetCollectionId));
    }
}
