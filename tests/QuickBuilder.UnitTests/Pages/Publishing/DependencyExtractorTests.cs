using QuickBuilder.Pages.Domain;

namespace QuickBuilder.UnitTests.Pages.Publishing;

/// <summary>What a published document depends on (012 research PR-04, data-model §1.3).</summary>
public sealed class DependencyExtractorTests
{
    private static readonly Guid Header = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-000000000001");
    private static readonly Guid Contact = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-000000000002");
    private static readonly Guid Pricing = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-000000000003");
    private static readonly Guid Photo = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-000000000004");

    private static string Document(string children) =>
        $$$"""{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":{{{children}}}}}""";

    [Fact]
    public void Components_page_links_and_media_are_found_at_any_depth()
    {
        var document = Document("""
            [{"id":"h","type":"component-ref","props":{"componentId":"HEADER"},"children":[]},
             {"id":"s","type":"section","props":{},"children":[
               {"id":"c","type":"container","props":{},"children":[
                 {"id":"b","type":"button","props":{"label":"Go","link":{"type":"page","pageId":"CONTACT"}},"children":[]},
                 {"id":"i","type":"image","props":{"media":{"mediaId":"PHOTO"},"link":{"type":"page","pageId":"PRICING"}},"children":[]},
                 {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[
                   {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[
                     {"type":"text","text":"x","marks":[{"type":"link","attrs":{"to":{"type":"page","pageId":"PRICING"}}}]}]}]}]}]}},"children":[]}]}]}]
            """
            .Replace("HEADER", Header.ToString(), StringComparison.Ordinal)
            .Replace("CONTACT", Contact.ToString(), StringComparison.Ordinal)
            .Replace("PHOTO", Photo.ToString(), StringComparison.Ordinal)
            .Replace("PRICING", Pricing.ToString(), StringComparison.Ordinal));

        Assert.Equal(
            [
                (DependencyKinds.Component, Header),
                (DependencyKinds.Media, Photo),
                (DependencyKinds.Page, Contact),
                (DependencyKinds.Page, Pricing),
            ],
            DependencyExtractor.Extract(document));
    }

    [Fact]
    public void External_links_and_empty_values_are_not_dependencies()
    {
        var document = Document("""
            [{"id":"s","type":"section","props":{},"children":[
              {"id":"b","type":"button","props":{"label":"Out","link":{"type":"url","href":"https://example.com"}},"children":[]},
              {"id":"i","type":"image","props":{},"children":[]},
              {"id":"e","type":"embed","props":{"url":"https://vimeo.com/1"},"children":[]}]}]
            """);

        Assert.Empty(DependencyExtractor.Extract(document));
    }

    [Fact]
    public void Components_lists_only_the_components()
    {
        var document = Document($$$"""
            [{"id":"a","type":"component-ref","props":{"componentId":"{{{Header}}}"},"children":[]},
             {"id":"b","type":"component-ref","props":{"componentId":"{{{Header}}}"},"children":[]}]
            """);

        Assert.Equal([Header], DependencyExtractor.Components(document));
    }

    [Fact]
    public void A_list_depends_on_its_collection_and_a_field_source_is_left_to_the_publisher()
    {
        var document = Document($$$"""
            [{"id":"l1","type":"collection-list","props":{"source":{"collectionId":"{{{Header}}}"},"paging":{"pageSize":5}},"children":[
               {"id":"l1-card","type":"list-card","props":{},"children":[
                 {"id":"l2","type":"collection-list","props":{"source":{"field":"f10"},"paging":{"pageSize":2,"param":"people"}},"children":[]}]}]}]
            """);

        Assert.Equal([(DependencyKinds.Collection, Header)], DependencyExtractor.Extract(document));
        Assert.Equal(["f10"], DependencyExtractor.ListSourceFields(document));
        Assert.Equal(["page", "people"], DependencyExtractor.PageParamNames(document).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_heading_link_to_a_page_is_a_dependency()
    {
        var page = Guid.Parse("0192f5a0-0000-7000-8000-0000000000aa");
        var document = Document($$$"""
            [{"id":"h","type":"heading","props":{"text":"Hi","level":2,"link":{"type":"page","pageId":"{{{page}}}"}},"children":[]}]
            """);

        Assert.Equal([(DependencyKinds.Page, page)], DependencyExtractor.Extract(document));
    }

    [Fact]
    public void Page_parameter_slots_point_at_each_list_and_component_reference_in_document_order()
    {
        // Rule 21 (016 FR-027): a refusal points at the list's param, its paging without one, or the reference.
        var document = Document($$$"""
            [{"id":"l1","type":"collection-list","props":{"source":{"collectionId":"{{{Header}}}"},"paging":{"pageSize":5}},"children":[
               {"id":"l1-card","type":"list-card","props":{},"children":[
                 {"id":"l2","type":"collection-list","props":{"source":{"field":"f10"},"paging":{"pageSize":2,"param":"people"}},"children":[]}]}]},
             {"id":"l3","type":"collection-list","props":{"source":{"collectionId":"{{{Header}}}"}},"children":[]},
             {"id":"ref","type":"component-ref","props":{"componentId":"{{{Header}}}"},"children":[]}]
            """);

        var slots = DependencyExtractor.PageParamSlots(document);

        Assert.Equal(
            [("page", "/root/children/0/props/paging"), ("people", "/root/children/0/children/0/children/0/props/paging/param")],
            slots.Lists);
        Assert.Equal([(Header, "/root/children/2")], slots.Components);
    }

    [Fact]
    public void A_document_without_lists_has_no_list_fields_or_page_parameters()
    {
        var document = Document("""[{"id":"h","type":"heading","props":{"text":"Hi","level":1},"children":[]}]""");

        Assert.Empty(DependencyExtractor.ListSourceFields(document));
        Assert.Empty(DependencyExtractor.PageParamNames(document));
    }
}
