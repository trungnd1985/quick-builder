using System.Collections.Concurrent;
using System.Text.Json;

namespace QuickBuilder.Rendering.Features.NotFound;

/// <summary>
/// The words visitors read on the not-found page (012 FR-030), on a list's pager (016
/// public-rendering.md §3) and in a Navigation block (024 public-navigation.md §4), in one
/// language.
/// </summary>
internal sealed record PublicTexts(string Title, string Message, string Home, PagerTexts Pager, NavigationTexts Navigation)
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
        Loaded.GetOrAdd(language ?? Fallback, tag => Read(tag) ?? Read(Fallback)
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
                navigation.GetProperty("newTab").GetString()!));
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
