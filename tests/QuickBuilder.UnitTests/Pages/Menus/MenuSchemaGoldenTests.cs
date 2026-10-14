using System.Text;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages.Menus;

/// <summary>
/// The menu document validator against the contract's golden files and the rules the files
/// cannot hold (024 research NV-03; docs/contracts/menu.md §4 and §7).
/// </summary>
/// <remarks>
/// The admin's <c>menuValidator.ts</c> runs the same files (<c>menuSchema.spec.ts</c>), so both
/// sides accept exactly the same menus (constitution Principle VI).
/// </remarks>
public sealed class MenuSchemaGoldenTests
{
    private static readonly MenuDocumentValidator Validator = new();

    public static TheoryData<string> Valid()
    {
        var data = new TheoryData<string>();
        foreach (var file in ContractFiles.ValidMenuExamples())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    public static TheoryData<string, string, string> Invalid()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (file, rule, pointer) in ContractFiles.InvalidMenuExamples())
        {
            data.Add(Path.GetFileName(file), rule, pointer);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void Every_valid_example_passes(string file)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(ContractFiles.MenuExamplesDirectory, "valid", file)));

        Assert.True(result.IsValid, $"{file}: {result.Rule} at '{result.Location}': {result.Message}");
        Assert.Equal(MenuSchema.CurrentVersion, result.SchemaVersion);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Every_invalid_example_fails_with_its_rule_and_pointer(string file, string rule, string location)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(ContractFiles.MenuExamplesDirectory, "invalid", file)));

        Assert.False(result.IsValid, $"{file} was accepted.");
        Assert.Equal((rule, location), (result.Rule, result.Location));
        Assert.StartsWith(rule, file, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_invalid_example_file_is_listed_in_the_manifest()
    {
        var listed = ContractFiles.InvalidMenuExamples().Select(e => Path.GetFileName(e.File)!).ToHashSet();
        var present = Directory.GetFiles(Path.Combine(ContractFiles.MenuExamplesDirectory, "invalid"), "*--*.json").Select(f => Path.GetFileName(f)!);

        Assert.All(present, file => Assert.Contains(file, listed));
    }

    [Fact]
    public void Failures_carry_the_menu_document_code()
    {
        var result = Validate("""{ "schemaVersion": 1, "entries": [ { "id": "x", "target": { "type": "none" }, "children": [] } ] }""");

        Assert.Equal(("pages.invalid_menu_document", "label_required"), (result.Code, result.Rule));
    }

    [Fact]
    public void An_unsupported_version_has_its_own_code()
    {
        var result = Validate("""{ "schemaVersion": 2, "entries": [] }""");

        Assert.Equal("pages.unsupported_schema_version", result.Code);
    }

    [Fact]
    public void A_document_over_the_size_limit_is_refused_before_it_is_parsed()
    {
        var bytes = new byte[MenuSchema.MaxDocumentBytes + 1];
        bytes.AsSpan().Fill((byte)' ');

        var result = Validator.Validate(bytes);

        Assert.Equal(("pages.document_too_large", "document_too_large", string.Empty), (result.Code, result.Rule, result.Location));
    }

    [Fact]
    public void Three_hundred_entries_pass_and_the_301st_is_refused_where_it_is()
    {
        Assert.True(Validate(Flat(300)).IsValid);

        var result = Validate(Flat(301));

        Assert.Equal(("too_many_entries", "/entries/300"), (result.Rule, result.Location));
    }

    [Fact]
    public void Entries_are_counted_in_document_order_across_levels()
    {
        // 150 top-level entries with one child each: the 301st is the 151st entry's child.
        var entries = string.Join(',', Enumerable.Range(0, 151).Select(i =>
            $$"""{ "id": "p{{i}}", "label": "P", "target": { "type": "none" }, "children": [ { "id": "c{{i}}", "label": "C", "target": { "type": "none" }, "children": [] } ] }"""));

        var result = Validate($$"""{ "schemaVersion": 1, "entries": [ {{entries}} ] }""");

        Assert.Equal(("too_many_entries", "/entries/150"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_pathologically_deep_document_is_too_deep_not_malformed()
    {
        var open = string.Concat(Enumerable.Range(0, 200).Select(i => $$"""{ "id": "e{{i}}", "label": "L", "target": { "type": "none" }, "children": ["""));
        var close = string.Concat(Enumerable.Repeat("] }", 200));

        var result = Validate($$"""{ "schemaVersion": 1, "entries": [ {{open}}{{close}} ] }""");

        Assert.Equal("too_deep", result.Rule);
    }

    [Fact]
    public void Messages_never_quote_labels_or_addresses()
    {
        var result = Validate("""{ "schemaVersion": 1, "entries": [ { "id": "x", "label": "SECRET-LABEL", "target": { "type": "url", "href": "javascript:SECRET" }, "children": [] } ] }""");

        Assert.False(result.IsValid);
        Assert.DoesNotContain("SECRET", result.Message, StringComparison.Ordinal);
    }

    private static string Flat(int count) =>
        $$"""{ "schemaVersion": 1, "entries": [ {{string.Join(',', Enumerable.Range(0, count).Select(i => $$"""{ "id": "e{{i}}", "label": "L", "target": { "type": "none" }, "children": [] }"""))}} ] }""";

    private static DocumentValidationResult Validate(string json) => Validator.Validate(Encoding.UTF8.GetBytes(json));
}
