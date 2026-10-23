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
    public void Every_language_has_every_form_message_with_the_english_placeholders()
    {
        // 018 public-forms.md §2.3: a rule message that lost {label} or {max} would tell a
        // visitor nothing about which field to fix.
        var english = PublicTexts.For(PublicTexts.Fallback).Forms;

        foreach (var tag in PublicTexts.Languages)
        {
            var forms = PublicTexts.For(tag).Forms;
            Assert.All(
                new[] { forms.Submit, forms.Success, forms.Error, forms.Limited, forms.Summary, forms.Required, forms.Trap },
                text => Assert.False(string.IsNullOrWhiteSpace(text), $"{tag} has an empty form text."));
            Assert.Equal(FormTexts.CodeKeys.Keys.Order(StringComparer.Ordinal), forms.Rules.Keys.Order(StringComparer.Ordinal));

            foreach (var (code, text) in forms.Rules)
            {
                Assert.True(
                    Placeholders(text) == Placeholders(english.Rules[code]),
                    $"{tag} {code}: '{text}' does not carry the placeholders of '{english.Rules[code]}'.");
            }
        }
    }

    [Fact]
    public void Every_language_has_the_notification_words_with_the_english_placeholders()
    {
        // 018 research FS-19: an email whose subject lost {form} would not say which form was sent.
        var english = Words(PublicTexts.For(PublicTexts.Fallback).Forms.Notification);

        foreach (var tag in PublicTexts.Languages)
        {
            var words = Words(PublicTexts.For(tag).Forms.Notification);
            foreach (var (name, text) in words)
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"{tag} has an empty notification text '{name}'.");
                Assert.True(
                    Placeholders(text) == Placeholders(english[name]),
                    $"{tag} {name}: '{text}' does not carry the placeholders of '{english[name]}'.");
            }
        }

        static Dictionary<string, string> Words(QuickBuilder.Rendering.Contracts.FormNotificationTexts t) => new(StringComparer.Ordinal)
        {
            ["subject"] = t.Subject,
            ["intro"] = t.Intro,
            ["page"] = t.Page,
            ["received"] = t.Received,
            ["open"] = t.Open,
            ["openWithoutLink"] = t.OpenWithoutLink,
            ["valuesOff"] = t.ValuesOff,
            ["footer"] = t.Footer,
        };
    }

    [Fact]
    public void A_language_without_a_file_falls_back_to_english()
    {
        Assert.Equal(PublicTexts.For("en"), PublicTexts.For("xx-Future"));
        Assert.Equal(PublicTexts.For("en"), PublicTexts.For(null));
        Assert.Equal("Seite nicht gefunden", PublicTexts.For("de").Title);
    }

    private static string Placeholders(string text) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, "\\{[a-z]+\\}").Select(m => m.Value).Order(StringComparer.Ordinal));

    private static IEnumerable<string> Keys(System.Text.Json.JsonElement element, string prefix = "") =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object
            ? element.EnumerateObject().SelectMany(p => Keys(p.Value, prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}"))
            : [prefix];
}
