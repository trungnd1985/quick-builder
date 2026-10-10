using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// The design token contract document, its schema and the code never disagree (011 research
/// ED-14, SC-009).
/// </summary>
public sealed partial class DesignTokenContractTests
{
    private static readonly string Contracts = Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts");

    private static string Document => File.ReadAllText(Path.Combine(Contracts, "design-tokens.md"));

    private static JsonElement Schema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts, "design-tokens", "v1.schema.json")));
        return schema.RootElement.Clone();
    }

    [Fact]
    public void The_embedded_schema_is_the_file_in_docs()
    {
        using var embedded = DesignTokens.OpenSchema(1);
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);

        Assert.Equal(File.ReadAllBytes(Path.Combine(Contracts, "design-tokens", "v1.schema.json")), copy.ToArray());
    }

    [Fact]
    public void The_embedded_defaults_are_the_golden_file()
    {
        Assert.Equal(
            File.ReadAllText(Path.Combine(Contracts, "design-tokens", "examples", "valid", "default.json")),
            DefaultDesignTokens.Json);
    }

    [Fact]
    public void The_schema_uses_only_the_page_schemas_subset()
    {
        // Both contracts are validated by the same keyword semantics (design-tokens.md §4).
        Assert.Empty(SchemaSubsetValidator.UnsupportedKeywords(Schema()));
    }

    [Fact]
    public void The_limits_in_the_schema_match_the_code()
    {
        var properties = Schema().GetProperty("properties");

        Assert.Equal(DesignTokens.MaxColors, properties.GetProperty("colors").GetProperty("maxItems").GetInt32());
        Assert.Equal(DesignTokens.MaxSizes, properties.GetProperty("typography").GetProperty("properties").GetProperty("sizes").GetProperty("maxItems").GetInt32());
        Assert.Equal(DesignTokens.MaxSpacing, properties.GetProperty("spacing").GetProperty("maxItems").GetInt32());
        Assert.Equal(DesignTokens.TokenIdPattern, Schema().GetProperty("$defs").GetProperty("tokenId").GetProperty("pattern").GetString());

        var width = Schema().GetProperty("$defs").GetProperty("contentWidth");
        Assert.Equal(DesignTokens.MinContentWidthPx, width.GetProperty("minimum").GetInt32());
        Assert.Equal(DesignTokens.MaxContentWidthPx, width.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public void The_rules_table_states_the_schemas_limits()
    {
        var rules = PageSchemaContractTests.Table(Document, "## 2. Rules").ToDictionary(r => r[0].Trim('`'), r => r[1], StringComparer.Ordinal);

        Assert.Contains($"1–{DesignTokens.MaxColors} entries", rules["colors"], StringComparison.Ordinal);
        Assert.Contains($"1–{DesignTokens.MaxSizes} entries", rules["typography.sizes"], StringComparison.Ordinal);
        Assert.Contains($"1–{DesignTokens.MaxSpacing} entries", rules["spacing"], StringComparison.Ordinal);
        Assert.Contains(DesignTokens.TokenIdPattern, rules["id"], StringComparison.Ordinal);
        Assert.Contains($"{DesignTokens.MaxDocumentBytes / 1024} KiB", rules["Size"], StringComparison.Ordinal);
        Assert.Contains($"from {DesignTokens.MinContentWidthPx} to {DesignTokens.MaxContentWidthPx}", rules["contentWidths"], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_example_the_document_lists_exists_and_every_invalid_one_is_in_the_manifest()
    {
        var examples = Path.Combine(Contracts, "design-tokens", "examples");
        var listed = PageSchemaContractTests.Table(Document, "## 5. Examples (golden files)").Select(r => r[0].Trim('`')).ToList();

        Assert.All(listed, file => Assert.True(File.Exists(Path.Combine(examples, file)), $"design-tokens.md §5 lists {file}, which does not exist."));

        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(examples, "invalid", "expected.json")));
        var manifest = expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var present = Directory.GetFiles(Path.Combine(examples, "invalid"), "*--*.json").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

        Assert.True(manifest.SetEquals(present!), "design-tokens/examples/invalid and expected.json disagree.");
        Assert.All(manifest, name => Assert.Contains($"invalid/{name}", listed));
    }

    [Fact]
    public void The_font_table_the_schema_enum_and_the_code_agree()
    {
        var documented = PageSchemaContractTests.Table(Document, "## 3. Font stacks")
            .Select(r => (Id: r[0].Trim('`'), Family: r[1].Trim('`')))
            .ToList();
        var enumerated = Schema().GetProperty("$defs").GetProperty("fontStack").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.Equal(FontStacks.All.Select(s => s.Key), documented.Select(d => d.Id));
        Assert.Equal(FontStacks.All.Select(s => s.Value), documented.Select(d => d.Family));
        Assert.Equal(FontStacks.All.Select(s => s.Key), enumerated);
    }

    [Fact]
    public void The_admins_font_stacks_match_the_code()
    {
        // The editor draws the canvas with these (research ED-17). Read as text: the admin
        // app is TypeScript, and this is the one place both lists are in view.
        var text = File.ReadAllText(Path.Combine(DraftApiTests.FindRepositoryRoot(), "src", "admin", "src", "editor", "tokens", "fontStacks.ts"));
        var admin = FontEntry().Matches(text).Select(m => new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value)).ToList();

        Assert.Equal(FontStacks.All, admin);
    }

    [Fact]
    public void The_admin_imports_the_default_set_rather_than_copying_it()
    {
        var text = File.ReadAllText(Path.Combine(DraftApiTests.FindRepositoryRoot(), "src", "admin", "src", "editor", "tokens", "defaultTokens.ts"));

        Assert.Contains("@contracts/design-tokens/examples/valid/default.json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_admins_page_schema_limits_match_the_contract()
    {
        var text = File.ReadAllText(Path.Combine(DraftApiTests.FindRepositoryRoot(), "src", "admin", "src", "editor", "schema", "limits.ts"));
        var constants = Constant().Matches(text).ToDictionary(
            m => m.Groups[1].Value,
            m => long.Parse(m.Groups[2].Value.Replace("_", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxDocumentBytes, constants["MAX_DOCUMENT_BYTES"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxNodes, constants["MAX_NODES"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxNodeDepth, constants["MAX_NODE_DEPTH"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxRichTextChars, constants["MAX_RICH_TEXT_CHARS"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxRichTextImages, constants["MAX_RICH_TEXT_IMAGES"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxRichTextTables, constants["MAX_RICH_TEXT_TABLES"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.CurrentVersion, constants["CURRENT_SCHEMA_VERSION"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxListsPerDocument, constants["MAX_LISTS_PER_DOCUMENT"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.MaxListDepth, constants["MAX_LIST_DEPTH"]);
        Assert.Equal(QuickBuilder.Pages.Contracts.PageSchema.CurrentBindingVersion, constants["CURRENT_BINDING_VERSION"]);
    }

    // ['system-ui', "system-ui, sans-serif"] — the id in single quotes, the family in double.
    [GeneratedRegex("\\['([a-z-]+)',\\s*\"([^\"]+)\"\\]")]
    private static partial Regex FontEntry();

    [GeneratedRegex("export const ([A-Z_]+) = ([0-9_]+)")]
    private static partial Regex Constant();
}
