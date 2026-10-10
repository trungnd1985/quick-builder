using System.Text;
using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Bindings and Collection list blocks in page documents: the binding contract's golden
/// documents and rules 13 and 15–20 (016 research TB-06; binding-expression.md §8).
/// </summary>
/// <remarks>
/// The admin's <c>documentValidator.spec.ts</c> runs the same files, so both validators give
/// the same rule and pointer for each.
/// </remarks>
public sealed class PageSchemaBindingTests
{
    private static readonly PageDocumentValidator Validator = new();

    private static string Examples => Path.Combine(ContractFiles.Root, "docs", "contracts", "binding-expression", "examples");

    public static TheoryData<string> Valid() =>
        [.. Directory.GetFiles(Path.Combine(Examples, "valid"), "*.json").Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];

    public static TheoryData<string, string, string> Invalid()
    {
        var data = new TheoryData<string, string, string>();
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Examples, "invalid", "expected.json")));
        foreach (var entry in expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
        {
            data.Add(entry.Name, entry.Value.GetProperty("rule").GetString()!, entry.Value.GetProperty("pointer").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void Every_valid_binding_example_passes(string file)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "valid", file)), DocumentKind.Page);

        Assert.True(result.IsValid, $"{file}: {result.Rule} at '{result.Location}': {result.Message}");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Every_invalid_binding_example_fails_with_its_rule_and_pointer(string file, string rule, string location)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "invalid", file)), DocumentKind.Page);

        Assert.False(result.IsValid, $"{file} was accepted.");
        Assert.Equal((rule, location), (result.Rule, result.Location));
        Assert.StartsWith(rule, file, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_invalid_binding_example_file_is_listed_in_the_manifest()
    {
        var listed = Invalid().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);
        var present = Directory.GetFiles(Path.Combine(Examples, "invalid"), "*--*.json").Select(f => Path.GetFileName(f)!);

        Assert.All(present, file => Assert.Contains(file, listed));
    }

    [Fact]
    public void A_list_source_collection_is_collected_for_the_caller_to_check_with_its_pointer()
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "valid", "collection-list-nested.json")), DocumentKind.Page);

        Assert.True(result.IsValid);
        Assert.Equal(
            new Dictionary<Guid, string>
            {
                [Guid.Parse("0192f5a0-0000-7000-8000-00000000c001")] = "/root/children/0/children/0/props/source",
                [Guid.Parse("0192f5a0-0000-7000-8000-00000000c002")] = "/root/children/0/children/2/props/source",
            },
            result.ReferencedCollections);
    }

    [Fact]
    public void A_page_scope_in_an_ordinary_page_is_saved_as_a_broken_binding_not_refused()
    {
        // Turning a template back into a page must never make its draft unsaveable (TB-06).
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "valid", "bindings-page-scope-in-page.json")), DocumentKind.Page);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void A_component_cannot_bind_to_the_page_item()
    {
        // Rule 16 (FR-029): outside any list, the nearest scope is the page's item.
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "valid", "bindings-every-prop.json")), DocumentKind.Component);

        Assert.Equal(("page_scope_not_allowed", "/root/children/0/children/0/props/bind/text/value"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_component_cannot_name_the_page_scope_inside_a_list()
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "valid", "collection-list-nested.json")), DocumentKind.Component);

        Assert.Equal(("page_scope_not_allowed", "/root/children/0/children/0/props/filters/0/value/scope"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_component_may_hold_a_list_whose_cards_bind_to_their_items()
    {
        var document = """
            { "schemaVersion": 1, "bindingVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [
              { "id": "s1", "type": "section", "props": {}, "children": [
                { "id": "latest", "type": "collection-list", "props": { "source": { "collectionId": "0192f5a0-0000-7000-8000-00000000c001" }, "limit": 3 }, "children": [
                  { "id": "card", "type": "list-card", "props": {}, "children": [
                    { "id": "t", "type": "heading", "props": { "text": "", "bind": { "text": { "value": { "field": "f1" } } } }, "children": [] } ] },
                  { "id": "empty", "type": "list-empty", "props": {}, "children": [] } ] } ] } ] } }
            """;

        var result = Validator.Validate(Encoding.UTF8.GetBytes(document), DocumentKind.Component);

        Assert.True(result.IsValid, $"{result.Rule} at '{result.Location}'");
    }

    [Fact]
    public void A_document_without_bindings_needs_no_binding_version_and_collects_no_collections()
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", "every-block.json")), DocumentKind.Page);

        Assert.True(result.IsValid);
        Assert.Empty(result.ReferencedCollections);
    }
}
