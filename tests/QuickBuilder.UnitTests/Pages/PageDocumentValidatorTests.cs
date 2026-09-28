using System.Text;
using System.Text.Json.Nodes;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// The page document validator against the contract's golden files and its own rules
/// (010 US3, FR-017, FR-018, FR-027, SC-003; docs/contracts/page-schema.md §3 and §8).
/// </summary>
public sealed class PageDocumentValidatorTests
{
    private static readonly PageDocumentValidator Validator = new();

    public static TheoryData<string> Valid()
    {
        var data = new TheoryData<string>();
        foreach (var file in ContractFiles.ValidExamples())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    public static TheoryData<string, string, string> Invalid()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (file, rule, location) in ContractFiles.InvalidExamples())
        {
            data.Add(Path.GetFileName(file), rule, location);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void Every_valid_example_passes(string file)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", file)), DocumentKind.Page);

        Assert.True(result.IsValid, $"{file}: {result.Rule} at '{result.Location}': {result.Message}");
        Assert.Equal(1, result.SchemaVersion);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Every_invalid_example_fails_with_its_rule_and_pointer(string file, string rule, string location)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "invalid", file)), DocumentKind.Page);

        Assert.False(result.IsValid, $"{file} was accepted.");
        Assert.Equal((rule, location), (result.Rule, result.Location));
        Assert.StartsWith(rule, file, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_invalid_example_file_is_listed_in_the_manifest()
    {
        var listed = ContractFiles.InvalidExamples().Select(e => Path.GetFileName(e.File)!).ToHashSet();
        var present = Directory.GetFiles(Path.Combine(ContractFiles.ExamplesDirectory, "invalid"), "*--*.json").Select(f => Path.GetFileName(f)!);

        Assert.All(present, file => Assert.Contains(file, listed));
    }

    [Fact]
    public void The_unsupported_version_example_has_its_own_code()
    {
        var result = Validator.Validate(
            File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "invalid", "unsupported_schema_version--version-2.json")),
            DocumentKind.Page);

        Assert.Equal("pages.unsupported_schema_version", result.Code);
    }

    [Fact]
    public void A_document_nested_33_deep_is_too_deep()
    {
        var result = Validate(Nested(33), DocumentKind.Page);

        Assert.Equal("too_deep", result.Rule);
        Assert.Equal("pages.invalid_document", result.Code);
    }

    [Fact]
    public void Depth_is_checked_before_the_schema()
    {
        // Nested component-refs break the schema too (they take no children), but the
        // structural walk runs first, so the reason is the depth (§3 order).
        var result = Validate(Nested(40), DocumentKind.Page);

        Assert.Equal("too_deep", result.Rule);
        Assert.StartsWith("/root/children/0/children/0", result.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_deeper_than_the_parser_allows_is_too_deep_not_malformed()
    {
        var json = new string('[', 300) + new string(']', 300);

        var result = Validator.Validate(Encoding.UTF8.GetBytes(json), DocumentKind.Page);

        Assert.Equal("too_deep", result.Rule);
    }

    [Fact]
    public void A_document_with_5001_nodes_has_too_many()
    {
        var children = new JsonArray();
        for (var i = 0; i < PageSchema.MaxNodes; i++)
        {
            children.Add(Reference($"n{i}"));
        }

        var result = Validate(Envelope(children), DocumentKind.Page);

        Assert.Equal(("too_many_nodes", "/root/children/4999"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_document_of_5000_nodes_is_accepted()
    {
        var children = new JsonArray();
        for (var i = 0; i < PageSchema.MaxNodes - 1; i++)
        {
            children.Add(Reference($"n{i}"));
        }

        Assert.True(Validate(Envelope(children), DocumentKind.Page).IsValid);
    }

    [Fact]
    public void A_document_over_one_mebibyte_is_too_large_before_it_is_parsed()
    {
        var bytes = new byte[PageSchema.MaxDocumentBytes + 1];
        Array.Fill(bytes, (byte)' ');

        var result = Validator.Validate(bytes, DocumentKind.Page);

        Assert.Equal("pages.document_too_large", result.Code);
    }

    [Fact]
    public void A_component_document_may_not_reference_a_component()
    {
        var result = Validate(Envelope(new JsonArray(Reference("header"))), DocumentKind.Component);

        Assert.Equal(("component_ref_not_allowed", "/root/children/0"), (result.Rule, result.Location));
        Assert.True(Validate(Envelope(new JsonArray()), DocumentKind.Component).IsValid);
    }

    [Fact]
    public void Referenced_components_are_collected_once_each()
    {
        var id = Guid.CreateVersion7();
        var result = Validate(
            Envelope(new JsonArray(Reference("a", id), Reference("b", id), Reference("c"))),
            DocumentKind.Page);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.ReferencedComponentIds.Count);
        Assert.Contains(id, result.ReferencedComponentIds);
    }

    [Theory]
    [InlineData("[]", "schema_violation", "")]
    [InlineData("{\"schemaVersion\":1}", "schema_violation", "")]
    [InlineData("{\"schemaVersion\":1,\"root\":{\"id\":\"root\",\"type\":\"root\",\"props\":{},\"children\":[]},\"extra\":1}", "schema_violation", "/extra")]
    [InlineData("{\"schemaVersion\":1,\"root\":{\"id\":\"root\",\"type\":\"root\",\"props\":{},\"children\":[]}} trailing", "malformed_json", "")]
    [InlineData("{\"schemaVersion\":1.5,\"root\":{\"id\":\"root\",\"type\":\"root\",\"props\":{},\"children\":[]}}", "schema_violation", "/schemaVersion")]
    [InlineData("{\"schemaVersion\":1,\"root\":{\"id\":\"root\",\"type\":\"root\",\"props\":{\"x\":1},\"children\":[]}}", "props_invalid", "/root/props/x")]
    [InlineData("{\"schemaVersion\":1,\"root\":{\"id\":\"root\",\"type\":\"root\",\"props\":{},\"children\":[1]}}", "unknown_type", "/root/children/0/type")]
    public void Other_malformed_documents_are_refused_where_they_break(string json, string rule, string location)
    {
        var result = Validator.Validate(Encoding.UTF8.GetBytes(json), DocumentKind.Page);

        Assert.Equal((rule, location), (result.Rule, result.Location));
    }

    [Fact]
    public void A_section_inside_a_section_is_not_allowed_there()
    {
        var result = Validate(Envelope(new JsonArray(Section("outer", Section("inner")))), DocumentKind.Page);

        Assert.Equal(("nesting_not_allowed", "/root/children/0/children/0/type"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_heading_directly_under_the_root_is_not_allowed_there()
    {
        var result = Validate(Envelope(new JsonArray(Heading("h", "Loose"))), DocumentKind.Page);

        Assert.Equal(("nesting_not_allowed", "/root/children/0/type"), (result.Rule, result.Location));
    }

    [Fact]
    public void A_rich_text_node_name_is_an_unknown_type_not_a_misplaced_one()
    {
        // 011 research ED-05 (analysis I2): `paragraph` appears in the schema as a rich-text
        // node, but it is not a node type, so 010's golden file keeps its result.
        var node = new JsonObject { ["id"] = "p", ["type"] = "paragraph", ["props"] = new JsonObject(), ["children"] = new JsonArray() };

        var result = Validate(Envelope(new JsonArray(node)), DocumentKind.Page);

        Assert.Equal(("unknown_type", "/root/children/0/type"), (result.Rule, result.Location));
    }

    [Fact]
    public void An_unknown_rich_text_mark_is_a_props_failure_at_its_type()
    {
        var content = RichText(new JsonObject { ["type"] = "text", ["text"] = "u", ["marks"] = new JsonArray(new JsonObject { ["type"] = "underline" }) });

        var result = Validate(Envelope(new JsonArray(Section("s", content))), DocumentKind.Page);

        Assert.Equal(
            ("props_invalid", "/root/children/0/children/0/props/content/content/0/content/0/marks/0/type"),
            (result.Rule, result.Location));
    }

    [Fact]
    public void Rich_text_over_the_limit_is_refused_across_text_nodes()
    {
        // 20,001 characters, no single text node over its own 20,000 limit.
        var content = RichText(
            new JsonObject { ["type"] = "text", ["text"] = new string('a', 10_000) },
            new JsonObject { ["type"] = "text", ["text"] = new string('b', 10_001) });

        var result = Validate(Envelope(new JsonArray(Section("s", content))), DocumentKind.Page);

        Assert.Equal(("rich_text_too_long", "/root/children/0/children/0/props/content"), (result.Rule, result.Location));
    }

    [Fact]
    public void Rich_text_at_the_limit_is_accepted_and_counts_code_points()
    {
        // 20,000 code points; the emoji are two UTF-16 units each, so a count in units would
        // wrongly refuse this.
        var content = RichText(
            new JsonObject { ["type"] = "text", ["text"] = string.Concat(Enumerable.Repeat("😀", 5_000)) },
            new JsonObject { ["type"] = "text", ["text"] = new string('a', 15_000) });

        Assert.True(Validate(Envelope(new JsonArray(Section("s", content))), DocumentKind.Page).IsValid);
    }

    [Fact]
    public void Every_010_example_keeps_its_result()
    {
        // 011 is additive within version 1 (contract §6): what passed still passes, and what
        // failed still fails the same way.
        string[] valid = ["empty.json", "header-and-footer.json"];
        Assert.All(valid, file => Assert.True(
            Validator.Validate(File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", file)), DocumentKind.Page).IsValid,
            file));

        var result = Validator.Validate(
            File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "invalid", "unknown_type--paragraph.json")),
            DocumentKind.Page);
        Assert.Equal(("unknown_type", "/root/children/0/type"), (result.Rule, result.Location));
    }

    [Fact]
    public void The_component_example_is_a_valid_component_document()
    {
        var result = Validator.Validate(
            File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", "component-page.json")),
            DocumentKind.Component);

        Assert.True(result.IsValid, $"{result.Rule} at {result.Location}");
    }

    [Fact]
    public void Messages_never_quote_the_document()
    {
        const string Canary = "zz-canary-content";
        var json = "{\"schemaVersion\":1,\"root\":{\"id\":\"root\",\"type\":\"" + Canary + "\",\"props\":{},\"children\":[]}}";

        var result = Validator.Validate(Encoding.UTF8.GetBytes(json), DocumentKind.Page);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(Canary, result.Message, StringComparison.Ordinal);
    }

    private static QuickBuilder.Pages.Contracts.DocumentValidationResult Validate(JsonNode document, DocumentKind kind) =>
        Validator.Validate(Encoding.UTF8.GetBytes(document.ToJsonString()), kind);

    private static JsonObject Envelope(JsonArray children) => new()
    {
        ["schemaVersion"] = 1,
        ["root"] = new JsonObject
        {
            ["id"] = "root",
            ["type"] = "root",
            ["props"] = new JsonObject(),
            ["children"] = children,
        },
    };

    private static JsonObject Reference(string id, Guid? componentId = null) => new()
    {
        ["id"] = id,
        ["type"] = "component-ref",
        ["props"] = new JsonObject { ["componentId"] = (componentId ?? Guid.CreateVersion7()).ToString() },
        ["children"] = new JsonArray(),
    };

    private static JsonObject Section(string id, params JsonNode[] children) => new()
    {
        ["id"] = id,
        ["type"] = "section",
        ["props"] = new JsonObject(),
        ["children"] = new JsonArray(children),
    };

    private static JsonObject Heading(string id, string text) => new()
    {
        ["id"] = id,
        ["type"] = "heading",
        ["props"] = new JsonObject { ["text"] = text },
        ["children"] = new JsonArray(),
    };

    /// <summary>A Rich Text block holding one paragraph of <paramref name="inline"/>.</summary>
    private static JsonObject RichText(params JsonNode[] inline) => new()
    {
        ["id"] = "rt",
        ["type"] = "rich-text",
        ["props"] = new JsonObject
        {
            ["content"] = new JsonObject
            {
                ["type"] = "doc",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(inline) }),
            },
        },
        ["children"] = new JsonArray(),
    };

    /// <summary>A root with component-refs nested inside each other, <paramref name="levels"/> nodes deep.</summary>
    private static JsonObject Nested(int levels)
    {
        var chain = new JsonArray();
        var current = chain;
        for (var level = 2; level <= levels; level++)
        {
            var node = Reference($"n{level}");
            current.Add(node);
            current = (JsonArray)node["children"]!;
        }

        return Envelope(chain);
    }
}
