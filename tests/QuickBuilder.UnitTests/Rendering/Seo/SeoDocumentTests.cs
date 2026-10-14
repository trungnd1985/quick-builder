using System.Text.Json;
using QuickBuilder.Rendering.Seo;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>
/// Reading a live document's search metadata (017 research SE-02, SE-05; seo-metadata.md §1).
/// </summary>
public sealed class SeoDocumentTests
{
    [Fact]
    public void A_document_without_metadata_has_none()
    {
        Assert.Null(SeoDocument.From(Document("{}")));
    }

    [Fact]
    public void Every_fixed_field_is_read()
    {
        var seo = SeoDocument.From(Document("""
            { "seo": {
                "title": "Web design", "description": "Fast sites.",
                "ogTitle": "Design", "ogDescription": "Fast.",
                "ogImage": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" },
                "canonical": { "type": "url", "href": "https://client.example/x" },
                "noindex": true, "noSitemap": true } }
            """))!;

        Assert.Equal("Web design", seo.Title);
        Assert.Equal("Fast sites.", seo.Description);
        Assert.Equal("Design", seo.OgTitle);
        Assert.Equal("Fast.", seo.OgDescription);
        Assert.Equal(Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f"), seo.OgImageId);
        Assert.Equal(new SeoCanonical(null, "https://client.example/x"), seo.Canonical);
        Assert.True(seo.NoIndex);
        Assert.True(seo.NoSitemap);
    }

    [Fact]
    public void A_page_canonical_names_the_page()
    {
        var seo = SeoDocument.From(Document("""
            { "seo": { "canonical": { "type": "page", "pageId": "0192f5a0-0000-7000-8000-000000000001" } } }
            """))!;

        Assert.Equal(new SeoCanonical(Guid.Parse("0192f5a0-0000-7000-8000-000000000001"), null), seo.Canonical);
    }

    [Fact]
    public void Empty_texts_count_as_absent()
    {
        var seo = SeoDocument.From(Document("""{ "seo": { "title": "  ", "description": "" } }"""))!;

        Assert.Null(seo.Title);
        Assert.Null(seo.Description);
        Assert.False(seo.NoIndex);
    }

    [Fact]
    public void Bindings_fallbacks_and_structured_data_are_kept_raw_for_the_resolver()
    {
        var seo = SeoDocument.From(Document("""
            { "seo": {
                "bind": { "title": { "value": { "field": "f1" } } },
                "fallback": { "description": "Stories from Acme." },
                "structuredData": { "type": "Article", "bind": { "headline": { "value": { "field": "f1" } } } } } }
            """))!;

        Assert.True(seo.Bindings.ContainsKey("title"));
        Assert.Equal("f1", seo.Bindings["title"].GetProperty("value").GetProperty("field").GetString());
        Assert.Equal("Stories from Acme.", seo.Fallbacks["description"]);
        Assert.Equal("Article", seo.StructuredDataType);
        Assert.True(seo.StructuredDataBindings.ContainsKey("headline"));
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        // A document is validated on save; reading never fails on something newer.
        var seo = SeoDocument.From(Document("""{ "seo": { "title": "A", "later": 1 } }"""))!;

        Assert.Equal("A", seo.Title);
    }

    [Fact]
    public void The_parsed_values_outlive_the_source_document()
    {
        SeoDocument seo;
        using (var parsed = JsonDocument.Parse("""
            { "schemaVersion": 1, "root": { "id": "root", "type": "root",
              "props": { "seo": { "bind": { "title": { "value": { "field": "f1" } } } } }, "children": [] } }
            """))
        {
            seo = SeoDocument.From(parsed.RootElement)!;
        }

        Assert.Equal("f1", seo.Bindings["title"].GetProperty("value").GetProperty("field").GetString());
    }

    private static JsonElement Document(string props)
    {
        using var parsed = JsonDocument.Parse(
            $$"""{ "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {{props}}, "children": [] } }""");
        return parsed.RootElement.Clone();
    }
}
