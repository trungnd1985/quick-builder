using System.Text;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Search metadata in page documents (017 research SE-02; page-schema.md §3 rules 22–25).
/// </summary>
/// <remarks>
/// The shared golden files under <c>page-schema/examples/</c> run in
/// <see cref="PageDocumentValidatorTests"/>, all as page documents. The rules here need a
/// component document, or say something the files cannot.
/// </remarks>
public sealed class SeoSchemaGoldenTests
{
    private static readonly PageDocumentValidator Validator = new();

    private const string WithSeo = """
        { "schemaVersion": 1,
          "root": { "id": "root", "type": "root", "props": { "seo": { "title": "Header" } }, "children": [] } }
        """;

    [Fact]
    public void A_component_document_cannot_carry_metadata()
    {
        var result = Validate(WithSeo, DocumentKind.Component);

        Assert.Equal(("seo_in_component", "/root/props/seo"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_page_document_may_carry_metadata()
    {
        Assert.True(Validate(WithSeo, DocumentKind.Page).IsValid);
    }

    [Fact]
    public void A_component_with_an_empty_root_props_is_still_valid()
    {
        const string json = """
            { "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [] } }
            """;

        Assert.True(Validate(json, DocumentKind.Component).IsValid);
    }

    [Fact]
    public void Metadata_bindings_on_an_ordinary_page_are_kept_not_refused()
    {
        // Rule 23: the page scope outside a template is a broken binding, as for blocks.
        const string json = """
            { "schemaVersion": 1, "bindingVersion": 1,
              "root": { "id": "root", "type": "root",
                "props": { "seo": { "bind": { "title": { "value": { "field": "f1" } } },
                                    "structuredData": { "type": "Event", "bind": { "startDate": { "value": { "field": "f3" } } } } } },
                "children": [] } }
            """;

        Assert.True(Validate(json, DocumentKind.Page).IsValid);
    }

    [Fact]
    public void A_structured_data_mapping_alone_needs_the_binding_version()
    {
        const string json = """
            { "schemaVersion": 1,
              "root": { "id": "root", "type": "root",
                "props": { "seo": { "structuredData": { "type": "Article", "bind": { "headline": { "value": { "field": "f1" } } } } } },
                "children": [] } }
            """;

        var result = Validate(json, DocumentKind.Page);

        Assert.Equal(("binding_version_required", string.Empty), (result.Rule, result.Location));
    }

    [Fact]
    public void A_structured_data_type_without_bindings_needs_no_binding_version()
    {
        const string json = """
            { "schemaVersion": 1,
              "root": { "id": "root", "type": "root", "props": { "seo": { "structuredData": { "type": "Article" } } }, "children": [] } }
            """;

        Assert.True(Validate(json, DocumentKind.Page).IsValid);
    }

    [Fact]
    public void A_metadata_binding_names_exactly_one_value()
    {
        const string json = """
            { "schemaVersion": 1, "bindingVersion": 1,
              "root": { "id": "root", "type": "root",
                "props": { "seo": { "bind": { "title": { "value": { "field": "f1", "item": "slug" } } } } },
                "children": [] } }
            """;

        var result = Validate(json, DocumentKind.Page);

        Assert.Equal(("props_invalid", "/root/props/seo/bind/title/value"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_metadata_binding_cannot_name_a_list_scope()
    {
        // Lists sit below the root, so no list scope is ever open where metadata reads.
        const string json = """
            { "schemaVersion": 1, "bindingVersion": 1,
              "root": { "id": "root", "type": "root",
                "props": { "seo": { "bind": { "title": { "value": { "field": "f1", "scope": "posts" } } } } },
                "children": [] } }
            """;

        var result = Validate(json, DocumentKind.Page);

        Assert.Equal(("unknown_scope", "/root/props/seo/bind/title/value/scope"), (result.Rule, result.Location));
    }

    [Fact]
    public void An_unknown_structured_data_type_is_refused()
    {
        const string json = """
            { "schemaVersion": 1,
              "root": { "id": "root", "type": "root", "props": { "seo": { "structuredData": { "type": "Recipe" } } }, "children": [] } }
            """;

        var result = Validate(json, DocumentKind.Page);

        Assert.Equal(("props_invalid", "/root/props/seo/structuredData/type"), (result.Rule, result.Location));
    }

    private static DocumentValidationResult Validate(string json, DocumentKind kind) =>
        Validator.Validate(Encoding.UTF8.GetBytes(json), kind);
}
