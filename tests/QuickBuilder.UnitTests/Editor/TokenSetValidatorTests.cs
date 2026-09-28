using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Infrastructure;

namespace QuickBuilder.UnitTests.Editor;

/// <summary>
/// The design token validator against the contract's golden files and its own rule order
/// (011 data-model §2, docs/contracts/design-tokens.md §2 and §5).
/// </summary>
public sealed class TokenSetValidatorTests
{
    private static readonly TokenSetValidator Validator = new();

    private static string Examples { get; } = Path.Combine(FindRepositoryRoot(), "docs", "contracts", "design-tokens", "examples");

    public static TheoryData<string> Valid()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Examples, "valid"), "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    public static TheoryData<string, string, string> Invalid()
    {
        var data = new TheoryData<string, string, string>();
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Examples, "invalid", "expected.json")));
        foreach (var entry in expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
        {
            data.Add(entry.Name, entry.Value.GetProperty("code").GetString()!, entry.Value.GetProperty("pointer").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void Every_valid_example_passes(string file)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "valid", file)));

        Assert.True(result.IsSuccess, $"{file}: {result.Error?.Code}");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Every_invalid_example_fails_with_its_code_and_pointer(string file, string code, string location)
    {
        var result = Validator.Validate(File.ReadAllBytes(Path.Combine(Examples, "invalid", file)));

        Assert.True(result.IsFailure, $"{file} was accepted.");
        Assert.Equal((code, location), (result.Error!.Code, TokenSetValidator.PointerOf(result.Error)));
        Assert.StartsWith(code, file, StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_over_64_kib_is_too_large_before_it_is_parsed()
    {
        var bytes = new byte[DesignTokens.MaxDocumentBytes + 1];
        Array.Fill(bytes, (byte)' ');

        Assert.Equal("editor.tokens_too_large", Validator.Validate(bytes).Error!.Code);
    }

    [Fact]
    public void Malformed_json_is_an_invalid_token_set()
    {
        Assert.Equal("editor.invalid_tokens", Validator.Validate(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,")).Error!.Code);
    }

    [Theory]
    [InlineData("colors", "/colors")]
    [InlineData("spacing", "/spacing")]
    public void An_empty_list_is_its_own_reason_even_when_the_schema_also_fails(string list, string location)
    {
        // Deleting the last entry must say so, not report some unrelated schema failure that
        // happens to come first (design-tokens.md §2, step 3).
        var set = Default();
        set[list] = new JsonArray();
        set["typography"]!["baseSizePx"] = 99;

        var result = Validator.Validate(Encoding.UTF8.GetBytes(set.ToJsonString()));

        Assert.Equal(("editor.token_required", location), (result.Error!.Code, TokenSetValidator.PointerOf(result.Error)));
    }

    [Fact]
    public void An_empty_text_size_list_is_its_own_reason()
    {
        var set = Default();
        set["typography"]!["sizes"] = new JsonArray();

        var result = Validator.Validate(Encoding.UTF8.GetBytes(set.ToJsonString()));

        Assert.Equal(("editor.token_required", "/typography/sizes"), (result.Error!.Code, TokenSetValidator.PointerOf(result.Error)));
    }

    [Fact]
    public void The_same_name_in_different_lists_is_fine()
    {
        // "Small" is both a text size and a spacing step in the defaults.
        Assert.True(Validator.Validate(Encoding.UTF8.GetBytes(Default().ToJsonString())).IsSuccess);
    }

    [Fact]
    public void An_id_duplicated_within_a_list_is_found_at_its_second_occurrence()
    {
        var set = Default();
        set["colors"]![2]!["id"] = "color-brand";

        var result = Validator.Validate(Encoding.UTF8.GetBytes(set.ToJsonString()));

        Assert.Equal(("editor.duplicate_token_id", "/colors/2/id"), (result.Error!.Code, TokenSetValidator.PointerOf(result.Error)));
    }

    [Theory]
    [InlineData(720, 1200, 1100)]
    [InlineData(1300, 1200, 1440)]
    public void Content_widths_out_of_order_are_refused(int narrow, int normal, int wide)
    {
        var set = Default();
        set["contentWidths"] = new JsonObject { ["narrow"] = narrow, ["normal"] = normal, ["wide"] = wide };

        var result = Validator.Validate(Encoding.UTF8.GetBytes(set.ToJsonString()));

        Assert.Equal(("editor.content_widths_order", "/contentWidths"), (result.Error!.Code, TokenSetValidator.PointerOf(result.Error)));
    }

    [Fact]
    public void Equal_content_widths_are_in_order()
    {
        var set = Default();
        set["contentWidths"] = new JsonObject { ["narrow"] = 960, ["normal"] = 960, ["wide"] = 960 };

        Assert.True(Validator.Validate(Encoding.UTF8.GetBytes(set.ToJsonString())).IsSuccess);
    }

    [Fact]
    public void A_set_without_content_widths_is_valid()
    {
        // Saved before content widths existed (011 spec, Edge Cases).
        var set = Default();
        set.Remove("contentWidths");

        Assert.True(Validator.Validate(Encoding.UTF8.GetBytes(set.ToJsonString())).IsSuccess);
    }

    [Fact]
    public void The_validated_version_is_reported()
    {
        var result = Validator.Validate(Encoding.UTF8.GetBytes(Default().ToJsonString()));

        Assert.Equal(1, result.Value);
    }

    private static JsonObject Default() => JsonNode.Parse(DefaultDesignTokens.Json)!.AsObject();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (QuickBuilder.slnx) was not found above the test binaries.");
    }
}
