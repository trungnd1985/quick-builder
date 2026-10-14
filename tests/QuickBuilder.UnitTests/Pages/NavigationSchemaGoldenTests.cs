using System.Text;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// The <c>navigation</c> node type in page and component documents (024 research NV-05;
/// page-schema.md §2, §3).
/// </summary>
/// <remarks>
/// The shared golden files under <c>page-schema/examples/</c> run in
/// <see cref="PageDocumentValidatorTests"/> as page documents. These cases need a component
/// document, or say something the files cannot.
/// </remarks>
public sealed class NavigationSchemaGoldenTests
{
    private static readonly PageDocumentValidator Validator = new();

    [Fact]
    public void The_component_example_is_valid_as_a_component()
    {
        var result = Validator.Validate(
            File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", "component-with-navigation.json")),
            DocumentKind.Component);

        Assert.True(result.IsValid, $"{result.Rule} at '{result.Location}'");
    }

    [Fact]
    public void A_menu_id_is_not_a_site_rule_like_a_component_id()
    {
        // A menu deleted later must never make the draft unsaveable (page-schema.md §3).
        var result = Validate(Navigation("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e99" }"""));

        Assert.True(result.IsValid);
        Assert.Empty(result.ReferencedComponentIds);
    }

    [Theory]
    [InlineData("""{ }""", "/root/children/0/children/0/props")]
    [InlineData("""{ "menuId": "not-a-uuid" }""", "/root/children/0/children/0/props/menuId")]
    [InlineData("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40", "layout": "grid" }""", "/root/children/0/children/0/props/layout")]
    [InlineData("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40", "levels": 0 }""", "/root/children/0/children/0/props/levels")]
    [InlineData("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40", "collapse": { "enabled": true } }""", "/root/children/0/children/0/props/collapse")]
    [InlineData("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40", "collapse": { "enabled": true, "below": "mobile", "buttonLabel": "" } }""", "/root/children/0/children/0/props/collapse/buttonLabel")]
    [InlineData("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40", "ariaLabel": "" }""", "/root/children/0/children/0/props/ariaLabel")]
    [InlineData("""{ "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40", "bind": { } }""", "/root/children/0/children/0/props/bind")]
    public void Props_outside_the_definition_are_refused_where_they_are(string props, string location)
    {
        var result = Validate(Navigation(props));

        Assert.Equal(("props_invalid", location), (result.Rule, result.Location));
    }

    private static string Navigation(string props) =>
        $$"""
        { "schemaVersion": 1,
          "root": { "id": "root", "type": "root", "props": {}, "children": [
            { "id": "s", "type": "section", "props": {}, "children": [
              { "id": "n", "type": "navigation", "props": {{props}}, "children": [] } ] } ] } }
        """;

    private static DocumentValidationResult Validate(string json) =>
        Validator.Validate(Encoding.UTF8.GetBytes(json), DocumentKind.Page);
}
