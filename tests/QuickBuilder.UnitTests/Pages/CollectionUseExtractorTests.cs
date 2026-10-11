using QuickBuilder.Pages.Domain;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Which collections a document uses and which of their fields it reads (016 data-model §1.3,
/// research TB-13; tasks T081).
/// </summary>
public sealed class CollectionUseExtractorTests
{
    private static readonly Guid Posts = Guid.Parse("0192f5a0-0000-7000-8000-00000000c001");
    private static readonly Guid People = Guid.Parse("0192f5a0-0000-7000-8000-00000000c002");

    /// <summary>Posts' f9 refers to a person, f10 to people; people refer to nothing.</summary>
    private static Guid? Target(Guid collection, string field) =>
        collection == Posts && field is "f9" or "f10" ? People : null;

    private static Dictionary<Guid, string[]> Uses(string? document, Guid? template = null) =>
        CollectionUseExtractor.Extract(document, template, Target).ToDictionary(u => u.Key, u => u.Value.ToArray());

    private static string Document(string blocks) =>
        """{"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[""" + blocks + "]}}";

    [Fact]
    public void A_template_uses_its_collection_even_with_no_document()
    {
        Assert.Equal(new Dictionary<Guid, string[]> { [Posts] = [] }, Uses(null, Posts));
    }

    [Fact]
    public void Page_scope_bindings_read_the_template_collection_and_via_reads_the_target_too()
    {
        var uses = Uses(Document("""
            {"id":"h","type":"heading","props":{"text":"x","bind":{"text":{"value":{"parts":[{"field":"f1"}," by ",{"field":"f1","via":"f9"}]}}}},"children":[]}
            """), Posts);

        Assert.Equal(["f1", "f9"], uses[Posts]);
        Assert.Equal(["f1"], uses[People]);
    }

    [Fact]
    public void A_list_uses_its_collection_with_its_filter_and_sort_fields_and_the_scope_fields_its_values_read()
    {
        var uses = Uses(Document($$$$$"""
            {"id":"l","type":"collection-list","props":{"source":{"collectionId":"{{{{{Posts}}}}}"},
              "filters":[{"field":"f8","op":"eq","value":{"field":"f8","scope":"page"}},{"item":"slug","op":"ne","value":{"current":true}}],
              "sort":[{"field":"f5","dir":"desc"}]},"children":[
              {"id":"c","type":"list-card","props":{},"children":[
                {"id":"t","type":"heading","props":{"text":"x","bind":{"text":{"value":{"field":"f1"}}}},"children":[]}]},
              {"id":"e","type":"list-empty","props":{},"children":[]}]}
            """), Posts);

        Assert.Equal(["f1", "f5", "f8"], uses[Posts]);
        Assert.Single(uses);
    }

    [Fact]
    public void A_list_over_a_multi_reference_field_uses_the_field_and_its_target_and_its_card_reads_the_target()
    {
        var uses = Uses(Document("""
            {"id":"l","type":"collection-list","props":{"source":{"field":"f10"}},"children":[
              {"id":"c","type":"list-card","props":{},"children":[
                {"id":"n","type":"heading","props":{"text":"x","bind":{"text":{"value":{"field":"f1"}}}},"children":[]},
                {"id":"outer","type":"heading","props":{"text":"x","bind":{"text":{"value":{"field":"f2","scope":"page"}}}},"children":[]}]},
              {"id":"e","type":"list-empty","props":{},"children":[]}]}
            """), Posts);

        Assert.Equal(["f10", "f2"], uses[Posts]);
        Assert.Equal(["f1"], uses[People]);
    }

    [Fact]
    public void A_component_without_a_page_scope_reads_only_its_lists()
    {
        var uses = Uses(Document($$$$$"""
            {"id":"h","type":"heading","props":{"text":"x","bind":{"text":{"value":{"field":"f1"}}}},"children":[]},
            {"id":"l","type":"collection-list","props":{"source":{"collectionId":"{{{{{People}}}}}"}},"children":[
              {"id":"c","type":"list-card","props":{},"children":[]},{"id":"e","type":"list-empty","props":{},"children":[]}]}
            """));

        Assert.Equal(new Dictionary<Guid, string[]> { [People] = [] }, uses);
    }

    [Fact]
    public void A_document_without_bindings_or_lists_uses_nothing()
    {
        Assert.Empty(Uses(Document("""{"id":"h","type":"heading","props":{"text":"x"},"children":[]}""")));
    }
}
