using System.Collections.Concurrent;
using System.Text.Json;
using QuickBuilder.Rendering.Contracts;

namespace QuickBuilder.Rendering.Features.NotFound;

/// <summary>
/// The words visitors read on the not-found page (012 FR-030), on a list's pager (016
/// public-rendering.md §3), in a Navigation block (024 public-navigation.md §4) and in a form
/// (018 public-forms.md §2.3), in one
/// language.
/// </summary>
internal sealed record PublicTexts(string Title, string Message, string Home, PagerTexts Pager, NavigationTexts Navigation, FormTexts Forms)
{
    /// <summary>The language every site falls back to.</summary>
    public const string Fallback = "en";

    private const string ResourcePrefix = "QuickBuilder.Rendering.PublicTexts.";

    private static readonly ConcurrentDictionary<string, PublicTexts> Loaded = new(StringComparer.Ordinal);

    /// <summary>
    /// The texts for <paramref name="language"/>, a tag from the Sites module's supported list,
    /// or English when the catalogue has no file for it: a language added to Sites later still
    /// gets a not-found page (research PR-13).
    /// </summary>
    public static PublicTexts For(string? language) =>
        Loaded.GetOrAdd(language ?? Fallback, tag => Read(tag) ?? English());

    // The one English instance, so a language that falls back gets exactly it (018: FormTexts
    // holds a dictionary, which records compare by reference).
    private static PublicTexts English() =>
        Loaded.GetOrAdd(Fallback, tag => Read(tag)
            ?? throw new InvalidOperationException("The English public texts are missing from the build."));

    /// <summary>The tags the catalogue has a file for.</summary>
    public static IReadOnlyList<string> Languages { get; } =
        [.. typeof(PublicTexts).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^".json".Length])
            .Order(StringComparer.Ordinal)];

    /// <summary>The raw catalogue file for a tag, for the catalogue tests; null when there is none.</summary>
    public static JsonDocument? Raw(string tag)
    {
        using var stream = typeof(PublicTexts).Assembly.GetManifestResourceStream($"{ResourcePrefix}{tag}.json");
        return stream is null ? null : JsonDocument.Parse(stream);
    }

    private static PublicTexts? Read(string tag)
    {
        using var document = Raw(tag);
        if (document is null)
        {
            return null;
        }

        var notFound = document.RootElement.GetProperty("notFound");
        var pager = document.RootElement.GetProperty("pager");
        var navigation = document.RootElement.GetProperty("navigation");
        var forms = document.RootElement.GetProperty("forms");
        return new PublicTexts(
            notFound.GetProperty("title").GetString()!,
            notFound.GetProperty("message").GetString()!,
            notFound.GetProperty("home").GetString()!,
            new PagerTexts(
                pager.GetProperty("label").GetString()!,
                pager.GetProperty("previous").GetString()!,
                pager.GetProperty("next").GetString()!,
                pager.GetProperty("first").GetString()!),
            new NavigationTexts(
                navigation.GetProperty("menu").GetString()!,
                navigation.GetProperty("showSubmenu").GetString()!,
                navigation.GetProperty("newTab").GetString()!),
            FormTexts.Read(forms));
    }
}

/// <summary>A pager's words: its name for screen readers, its two step links, and the way back from past the end.</summary>
internal sealed record PagerTexts(string Label, string Previous, string Next, string First);

/// <summary>
/// A Navigation block's words (024): the collapsed menu's button, the name of a submenu's
/// open/close button (<c>{0}</c> is the entry's label), and what a link that opens a new tab
/// tells assistive technology.
/// </summary>
internal sealed record NavigationTexts(string Menu, string ShowSubmenu, string NewTab);

/// <summary>
/// A form's words (018 public-forms.md §2.3): its default button, messages and summary heading,
/// the required marker, the trap field's label, one message per value code with
/// <c>{label}</c>, <c>{min}</c> and <c>{max}</c> to fill in, and the notification email's words
/// (research FS-19).
/// </summary>
internal sealed record FormTexts(
    string Submit,
    string Success,
    string Error,
    string Limited,
    string Summary,
    string Required,
    string Trap,
    IReadOnlyDictionary<string, string> Rules,
    FormNotificationTexts Notification)
{
    /// <summary>The catalogue's rule keys, by value code (public-forms.md §4).</summary>
    public static IReadOnlyDictionary<string, string> CodeKeys { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["required"] = "required",
        ["too_short"] = "tooShort",
        ["too_long"] = "tooLong",
        ["email"] = "email",
        ["phone"] = "phone",
        ["number"] = "number",
        ["whole"] = "whole",
        ["too_small"] = "tooSmall",
        ["too_large"] = "tooLarge",
        ["date"] = "date",
        ["too_early"] = "tooEarly",
        ["too_late"] = "tooLate",
        ["choice"] = "choice",
        ["too_few"] = "tooFew",
        ["too_many"] = "tooMany",
    };

    /// <summary>The message for a value code with its placeholders left in; the generic required text for an unknown code.</summary>
    public string Template(string code) =>
        Rules.TryGetValue(code, out var text) ? text : Rules["required"];

    internal static FormTexts Read(JsonElement forms)
    {
        var rules = forms.GetProperty("rules");
        var notification = forms.GetProperty("notification");
        return new FormTexts(
            forms.GetProperty("submit").GetString()!,
            forms.GetProperty("success").GetString()!,
            forms.GetProperty("error").GetString()!,
            forms.GetProperty("limited").GetString()!,
            forms.GetProperty("summary").GetString()!,
            forms.GetProperty("required").GetString()!,
            forms.GetProperty("trap").GetString()!,
            CodeKeys.ToDictionary(c => c.Key, c => rules.GetProperty(c.Value).GetString()!, StringComparer.Ordinal),
            new FormNotificationTexts(
                notification.GetProperty("subject").GetString()!,
                notification.GetProperty("intro").GetString()!,
                notification.GetProperty("page").GetString()!,
                notification.GetProperty("received").GetString()!,
                notification.GetProperty("open").GetString()!,
                notification.GetProperty("openWithoutLink").GetString()!,
                notification.GetProperty("valuesOff").GetString()!,
                notification.GetProperty("footer").GetString()!));
    }
}

/// <summary>The notification words for Forms (018 FS-19), read from the public catalogue.</summary>
internal sealed class FormNotificationTextCatalogue : IFormNotificationTexts
{
    public FormNotificationTexts For(string? language) => PublicTexts.For(language).Forms.Notification;
}
