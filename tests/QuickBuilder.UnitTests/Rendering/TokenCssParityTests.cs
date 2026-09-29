using System.Text.Json;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// The renderer's token CSS equals the editor's, on the shared fixtures (012 research PR-10).
/// </summary>
/// <remarks>
/// <c>tokenCss.parity.spec.ts</c> runs the same files against <c>tokenCss.ts</c>, which is the
/// reference: its update mode writes <c>expected</c>.
/// </remarks>
public sealed class TokenCssParityTests
{
    public static TheoryData<string> Fixtures() =>
        [.. Directory.GetFiles(RepositoryFiles.Fixtures("token-css"), "*.json").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_custom_properties_match_the_editor(string file)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Fixtures("token-css", file)));
        var expected = fixture.RootElement.GetProperty("expected").EnumerateObject()
            .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString()!))
            .ToList();

        var actual = TokenCss.For(fixture.RootElement.GetProperty("tokens")).Properties();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_missing_token_falls_back_to_the_default_token_then_to_the_fallback()
    {
        using var design = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Path("docs", "contracts", "design-tokens", "examples", "valid", "default.json")));
        var css = TokenCss.For(design.RootElement);

        using var refs = JsonDocument.Parse("""{"present":{"token":"color-brand"},"gone":{"token":"color-gone"},"fallback":{"token":"color-text"}}""");
        var root = refs.RootElement;

        Assert.Equal("var(--qb-color-color-brand)", css.Value(root.GetProperty("present"), TokenKind.Color, null));
        Assert.Equal("var(--qb-color-color-text)", css.Value(root.GetProperty("gone"), TokenKind.Color, root.GetProperty("fallback")));
        Assert.Equal("transparent", css.Value(root.GetProperty("gone"), TokenKind.Color, null, "transparent"));
        Assert.Equal("initial", css.Value(null, TokenKind.Space, null));
    }

    [Fact]
    public void A_colour_that_could_break_out_of_the_style_element_is_dropped()
    {
        using var design = JsonDocument.Parse("""
            {"schemaVersion":1,"colors":[{"id":"color-bad","name":"Bad","value":"red}</style><script>x</script>"}],
             "typography":{"headingFont":"system-ui","bodyFont":"system-ui","baseSizePx":16,"sizes":[]},"spacing":[]}
            """);

        var properties = TokenCss.For(design.RootElement).Properties();

        Assert.DoesNotContain(properties, p => p.Key == "--qb-color-color-bad");
        Assert.DoesNotContain(properties, p => p.Value.Contains('<', StringComparison.Ordinal));
    }
}
