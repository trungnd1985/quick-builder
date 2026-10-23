using System.Text.Json;
using QuickBuilder.Pages.Domain.Forms;

namespace QuickBuilder.UnitTests.Pages.Forms;

/// <summary>
/// A form's publish rules (page-schema.md §3 rule 28; 018 research FS-04), from the fixture the
/// admin's <c>formSetup.ts</c> runs too, so both report the same problems in the same order.
/// </summary>
public sealed class FormPublishRulesTests
{
    private static readonly string FixturePath =
        Path.Combine(ContractFiles.Root, "tests", "fixtures", "forms", "setup-rules.json");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var name in Fixture().Select(c => c.Name))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_problem_is_reported_in_the_shared_order(string name)
    {
        var (_, node, expected) = Fixture().Single(c => c.Name == name);

        var problems = FormPublishRules.Check(node);

        Assert.Equal(expected, problems.Select(p => (p.Rule, p.FieldId)));
    }

    [Fact]
    public void The_pages_a_form_needs_live_are_its_redirect_and_its_consent_links()
    {
        var node = Node("""
            { "name": "F",
              "fields": [
                { "id": "a00000000001", "type": "consent", "label": "A", "statement": "S", "linkPageId": "0192f5a0-0000-7000-8000-000000000001" },
                { "id": "a00000000002", "type": "consent", "label": "B", "statement": "S" },
                { "id": "a00000000003", "type": "consent", "label": "C", "statement": "S", "linkPageId": "0192f5a0-0000-7000-8000-000000000001" } ],
              "redirect": { "type": "page", "pageId": "0192f5a0-0000-7000-8000-000000000002" } }
            """);

        Assert.Equal(
            [Guid.Parse("0192f5a0-0000-7000-8000-000000000002"), Guid.Parse("0192f5a0-0000-7000-8000-000000000001")],
            FormPublishRules.LinkedPages(node));
    }

    [Fact]
    public void A_redirect_to_an_address_needs_no_live_page()
    {
        var node = Node("""{ "name": "F", "fields": [], "redirect": { "type": "url", "href": "https://example.com/" } }""");

        Assert.Empty(FormPublishRules.LinkedPages(node));
    }

    private static IEnumerable<(string Name, JsonElement Node, (string, string?)[] Problems)> Fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return (
                testCase.GetProperty("name").GetString()!,
                Node(testCase.GetProperty("props").GetRawText()),
                [.. testCase.GetProperty("problems").EnumerateArray().Select(p =>
                    (p.GetProperty("rule").GetString()!, p.GetProperty("fieldId").GetString()))]);
        }
    }

    private static JsonElement Node(string props)
    {
        using var node = JsonDocument.Parse($$"""{ "id": "f", "type": "form", "props": {{props}}, "children": [] }""");
        return node.RootElement.Clone();
    }
}
