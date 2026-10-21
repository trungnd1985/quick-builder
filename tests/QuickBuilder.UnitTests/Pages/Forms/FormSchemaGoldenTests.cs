using System.Text;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages.Forms;

/// <summary>
/// The <c>form</c> node type in page and component documents (018 research FS-02, FS-04;
/// page-schema.md §2, §3 rules 26–27).
/// </summary>
/// <remarks>
/// The shared golden files under <c>page-schema/examples/</c> run in
/// <see cref="PageDocumentValidatorTests"/> as page documents. These cases need a component
/// document, or say something the files cannot.
/// </remarks>
public sealed class FormSchemaGoldenTests
{
    private static readonly PageDocumentValidator Validator = new();

    [Fact]
    public void The_component_example_is_valid_as_a_component()
    {
        var result = Validator.Validate(
            File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", "form-in-component.json")),
            DocumentKind.Component);

        Assert.True(result.IsValid, $"{result.Rule} at '{result.Location}'");
    }

    [Fact]
    public void An_incomplete_form_is_saveable_because_only_publishing_refuses_it()
    {
        // Rule 28: no fields, duplicate labels and a missing reply field are publish rules.
        var result = Validate(Form("""
            { "name": "F", "fields": [
                { "id": "a1b2c3d4e5f6", "type": "text", "label": "Name" },
                { "id": "b1b2c3d4e5f6", "type": "text", "label": "NAME", "minLength": 9, "maxLength": 2 },
                { "id": "c1b2c3d4e5f6", "type": "select", "label": "Pick" } ],
              "notify": { "replyFieldId": "a1b2c3d4e5f6", "includeValues": false },
              "redirect": { "type": "page", "pageId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e99" } }
            """));

        Assert.True(result.IsValid, $"{result.Rule} at '{result.Location}'");
    }

    [Fact]
    public void Twenty_forms_are_allowed()
    {
        var forms = string.Join(",", Enumerable.Range(1, 20).Select(i =>
            $$"""{ "id": "f{{i}}", "type": "form", "props": { "name": "F", "fields": [] }, "children": [] }"""));

        var result = Validate($$"""
            { "schemaVersion": 1,
              "root": { "id": "root", "type": "root", "props": {}, "children": [
                { "id": "s", "type": "section", "props": {}, "children": [ {{forms}} ] } ] } }
            """);

        Assert.True(result.IsValid, $"{result.Rule} at '{result.Location}'");
    }

    [Fact]
    public void The_same_field_id_in_two_forms_is_allowed()
    {
        // Field ids are unique within their form only (rule 27); ids are keyed per form.
        var result = Validate($$"""
            { "schemaVersion": 1,
              "root": { "id": "root", "type": "root", "props": {}, "children": [
                { "id": "s", "type": "section", "props": {}, "children": [
                  { "id": "f1", "type": "form", "props": { "name": "A", "fields": [ { "id": "a1b2c3d4e5f6", "type": "email", "label": "Email" } ] }, "children": [] },
                  { "id": "f2", "type": "form", "props": { "name": "B", "fields": [ { "id": "a1b2c3d4e5f6", "type": "email", "label": "Email" } ] }, "children": [] }
                ] } ] } }
            """);

        Assert.True(result.IsValid, $"{result.Rule} at '{result.Location}'");
    }

    [Theory]
    [InlineData("""{ "fields": [] }""", "/root/children/0/children/0/props")]
    [InlineData("""{ "name": "", "fields": [] }""", "/root/children/0/children/0/props/name")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "A1B2C3D4E5F6", "type": "text", "label": "X" } ] }""", "/root/children/0/children/0/props/fields/0/id")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "a1b2c3d4e5f6", "type": "file", "label": "X" } ] }""", "/root/children/0/children/0/props/fields/0/type")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "a1b2c3d4e5f6", "type": "text", "label": "" } ] }""", "/root/children/0/children/0/props/fields/0/label")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "a1b2c3d4e5f6", "type": "text", "label": "X", "maxLength": 501 } ] }""", "/root/children/0/children/0/props/fields/0/maxLength")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "a1b2c3d4e5f6", "type": "date", "label": "X", "earliest": "2026-13-01" } ] }""", "/root/children/0/children/0/props/fields/0/earliest")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "a1b2c3d4e5f6", "type": "select", "label": "X", "options": [] } ] }""", "/root/children/0/children/0/props/fields/0/options")]
    [InlineData("""{ "name": "F", "fields": [ { "id": "a1b2c3d4e5f6", "type": "consent", "label": "X" } ] }""", "/root/children/0/children/0/props/fields/0")]
    [InlineData("""{ "name": "F", "fields": [], "notify": { "recipients": ["not an address"] } }""", "/root/children/0/children/0/props/notify/recipients/0")]
    [InlineData("""{ "name": "F", "fields": [], "notify": { "subject": "Hi\nBcc: x@y" } }""", "/root/children/0/children/0/props/notify/subject")]
    [InlineData("""{ "name": "F", "fields": [], "redirect": { "type": "url", "href": "mailto:x@y.example" } }""", "/root/children/0/children/0/props/redirect/href")]
    [InlineData("""{ "name": "F", "fields": [], "retentionDays": 60 }""", "/root/children/0/children/0/props/retentionDays")]
    public void Props_outside_the_definition_are_refused_where_they_are(string props, string location)
    {
        var result = Validate(Form(props));

        Assert.Equal(("props_invalid", location), (result.Rule, result.Location));
    }

    private static string Form(string props) =>
        $$"""
        { "schemaVersion": 1,
          "root": { "id": "root", "type": "root", "props": {}, "children": [
            { "id": "s", "type": "section", "props": {}, "children": [
              { "id": "f", "type": "form", "props": {{props}}, "children": [] } ] } ] } }
        """;

    private static DocumentValidationResult Validate(string json) =>
        Validator.Validate(Encoding.UTF8.GetBytes(json), DocumentKind.Page);
}
