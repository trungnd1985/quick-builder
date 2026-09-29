using System.Reflection;
using QuickBuilder.Rendering.Features.NotFound;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// Every language a site can have gets the not-found page in that language (012 FR-030,
/// SC-009, research PR-13).
/// </summary>
public sealed class PublicTextCatalogueTests
{
    /// <summary>The Sites module's supported tags, read from the module rather than copied here.</summary>
    private static IReadOnlyList<string> SupportedLanguages()
    {
        var type = typeof(QuickBuilder.Sites.SitesModule).Assembly.GetType("QuickBuilder.Sites.Domain.SupportedLanguages", throwOnError: true)!;
        var all = type.GetProperty("All", BindingFlags.Public | BindingFlags.Static)!;
        return (IReadOnlyList<string>)all.GetValue(null)!;
    }

    [Fact]
    public void Every_supported_language_has_a_file()
    {
        var missing = SupportedLanguages().Except(PublicTexts.Languages, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "No public texts for: " + string.Join(", ", missing));
        Assert.Equal(40, PublicTexts.Languages.Count);
    }

    [Fact]
    public void Every_file_has_exactly_the_english_keys_each_filled_in()
    {
        using var english = PublicTexts.Raw(PublicTexts.Fallback)!;
        var expected = Keys(english.RootElement).Order(StringComparer.Ordinal).ToList();

        foreach (var tag in PublicTexts.Languages)
        {
            using var file = PublicTexts.Raw(tag)!;
            Assert.Equal(expected, Keys(file.RootElement).Order(StringComparer.Ordinal).ToList());

            var texts = PublicTexts.For(tag);
            Assert.All(new[] { texts.Title, texts.Message, texts.Home }, text => Assert.False(string.IsNullOrWhiteSpace(text), $"{tag} has an empty text."));
        }
    }

    [Fact]
    public void A_language_without_a_file_falls_back_to_english()
    {
        Assert.Equal(PublicTexts.For("en"), PublicTexts.For("xx-Future"));
        Assert.Equal(PublicTexts.For("en"), PublicTexts.For(null));
        Assert.Equal("Seite nicht gefunden", PublicTexts.For("de").Title);
    }

    private static IEnumerable<string> Keys(System.Text.Json.JsonElement element, string prefix = "") =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object
            ? element.EnumerateObject().SelectMany(p => Keys(p.Value, prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}"))
            : [prefix];
}
