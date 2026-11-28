using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Editor.Contracts;

namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// A design as CSS custom properties, exactly as the editor's <c>tokenCss.ts</c> produces them
/// (012 research PR-10; page-schema.md §9).
/// </summary>
/// <remarks>
/// A port, kept honest by shared fixtures that both implementations run
/// (<c>tests/fixtures/rendering/token-css/</c>). Every id of the built-in default set is always
/// emitted, so a fallback to a property's default token always resolves.
/// </remarks>
internal sealed partial class TokenCss
{
    private static readonly TokenSet Defaults = TokenSet.From(DefaultDesignTokens.Document);

    private readonly TokenSet _set;

    private TokenCss(TokenSet set) => _set = set;

    /// <summary>The design's token CSS.</summary>
    public static TokenCss For(JsonElement design) => new(TokenSet.From(design));

    /// <summary>Every custom property, defaults first, the design's own overriding them.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Properties()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (name, value) in Entries(Defaults).Concat(Entries(_set)))
        {
            if (!map.ContainsKey(name))
            {
                order.Add(name);
            }

            map[name] = value;
        }

        return [.. order.Select(name => new KeyValuePair<string, string>(name, map[name]))];
    }

    /// <summary>
    /// A token reference as CSS: the token's variable, or the property default's when the token
    /// is gone (011 clarification Q4), or <paramref name="fallback"/>. Mirrors <c>tokenValue</c>.
    /// </summary>
    public string Value(JsonElement? reference, TokenKind kind, JsonElement? defaultReference, string fallback = "initial")
    {
        if (IdOf(reference) is { } id && _set.Has(kind, id))
        {
            return Variable(kind, id);
        }

        return IdOf(defaultReference) is { } fallbackId ? Variable(kind, fallbackId) : fallback;
    }

    /// <summary>
    /// A token reference as CSS only when it names a token of the design; otherwise null, so a
    /// caller can leave the declaration out and let a lower layer apply (024 research NV-26).
    /// </summary>
    public string? ValueIfSet(JsonElement? reference, TokenKind kind) =>
        IdOf(reference) is { } id && _set.Has(kind, id) ? Variable(kind, id) : null;

    /// <summary>The custom property name for a token id.</summary>
    public static string Variable(TokenKind kind, string id) => $"var(--qb-{Prefix(kind)}-{id})";

    /// <summary>
    /// The design's content widths in pixels, the defaults standing in for a set saved before
    /// they existed. The image renderer's <c>sizes</c> hint uses them (013 research MV-15).
    /// </summary>
    public (double Narrow, double Normal, double Wide) ContentWidths =>
        _set.ContentWidths ?? Defaults.ContentWidths ?? (720, 1_200, 1_440);

    /// <summary>
    /// The site's menu look (024 FR-017b), or null when the design does not choose one. A block's
    /// own <c>look</c> wins over it; with neither, menus use <c>link</c>.
    /// </summary>
    public string? MenuLook => _set.MenuLook;

    // 024 FR-017a (contracts/menu-style.md §2): the site layer every Navigation block falls back
    // to. Only colours the site chose, naming a token of its set, are written; the built-in
    // defaults are the stylesheet's last fallback, so a site without a menu style renders exactly
    // as before it existed. Mirrors tokenCss.ts.
    private static readonly (string Key, string Property)[] MenuProperties =
    [
        ("background", "--qb-menu-background"),
        ("text", "--qb-menu-text"),
        ("hoverText", "--qb-menu-hover-text"),
        ("currentText", "--qb-menu-current-text"),
        ("focus", "--qb-menu-focus"),
        ("panelBackground", "--qb-menu-panel-background"),
        ("hoverBackground", "--qb-menu-hover-background"),
        ("currentBackground", "--qb-menu-current-background"),
    ];

    private static string Prefix(TokenKind kind) => kind switch
    {
        TokenKind.Color => "color",
        TokenKind.Space => "space",
        TokenKind.Radius => "radius",
        _ => "text",
    };

    private static string? IdOf(JsonElement? reference) =>
        reference is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty("token", out var token)
        && token.ValueKind == JsonValueKind.String
        && TokenId().IsMatch(token.GetString()!)
            ? token.GetString()
            : null;

    private static IEnumerable<KeyValuePair<string, string>> Entries(TokenSet set)
    {
        foreach (var (id, value) in set.Colors)
        {
            yield return new($"--qb-color-{id}", value);
        }

        foreach (var (id, rem) in set.TextSizes)
        {
            yield return new($"--qb-text-{id}", Scale(rem));
        }

        foreach (var (id, rem) in set.Spacing)
        {
            yield return new($"--qb-space-{id}", Scale(rem));
        }

        // 066: corner radii scale like spacing. A set without its own adds none; the defaults' stand.
        foreach (var (id, rem) in set.Radii ?? [])
        {
            yield return new($"--qb-radius-{id}", Scale(rem));
        }

        yield return new("--qb-font-heading", FontStacks.FamilyOf(set.HeadingFont) ?? FontStacks.FamilyOf("system-ui")!);
        yield return new("--qb-font-body", FontStacks.FamilyOf(set.BodyFont) ?? FontStacks.FamilyOf("system-ui")!);
        yield return new("--qb-base-size", $"{Number(set.BaseSizePx)}px");

        // A set saved before content widths existed has none; the default set's stand.
        if ((set.ContentWidths ?? Defaults.ContentWidths) is { } widths)
        {
            yield return new("--qb-width-narrow", $"{Number(widths.Narrow)}px");
            yield return new("--qb-width-normal", $"{Number(widths.Normal)}px");
            yield return new("--qb-width-wide", $"{Number(widths.Wide)}px");
        }

        foreach (var (key, property) in MenuProperties)
        {
            if (set.MenuColors.TryGetValue(key, out var id) && set.Has(TokenKind.Color, id))
            {
                yield return new(property, $"var(--qb-color-{id})");
            }
        }
    }

    // Sizes scale with the site's base size, not the document's root size (tokenCss.ts).
    private static string Scale(double rem) => $"calc(var(--qb-base-size) * {Number(rem)})";

    // JavaScript's number-to-string for the values token sets hold: 1, 0.875, 16.
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[a-z0-9-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenId();

    /// <summary>The parts of a token set the CSS needs, read defensively.</summary>
    private sealed partial record TokenSet(
        IReadOnlyList<(string Id, string Value)> Colors,
        IReadOnlyList<(string Id, double Rem)> TextSizes,
        IReadOnlyList<(string Id, double Rem)> Spacing,
        IReadOnlyList<(string Id, double Rem)>? Radii,
        string HeadingFont,
        string BodyFont,
        double BaseSizePx,
        (double Narrow, double Normal, double Wide)? ContentWidths,
        IReadOnlyDictionary<string, string> MenuColors,
        string? MenuLook)
    {
        private static readonly HashSet<string> Looks = new(StringComparer.Ordinal) { "link", "underline", "pill", "button" };

        public bool Has(TokenKind kind, string id) => kind switch
        {
            TokenKind.Color => Colors.Any(c => c.Id == id),
            TokenKind.Space => Spacing.Any(s => s.Id == id),

            // A set saved before radii existed uses the default set's (design-tokens.md §2, 066).
            TokenKind.Radius => (Radii ?? Defaults.Radii ?? []).Any(r => r.Id == id),
            _ => TextSizes.Any(s => s.Id == id),
        };

        public static TokenSet From(JsonElement document)
        {
            var typography = Property(document, "typography");

            return new TokenSet(
                [.. Items(Property(document, "colors")).Select(c => (Id(c), String(c, "value")))
                    .Where(c => c.Item1 is not null && SafeColor().IsMatch(c.Item2)).Select(c => (c.Item1!, c.Item2))],
                [.. Scale(Property(typography, "sizes"))],
                [.. Scale(Property(document, "spacing"))],
                Property(document, "radii") is { ValueKind: JsonValueKind.Array } radii ? [.. Scale(radii)] : null,
                String(typography, "headingFont"),
                String(typography, "bodyFont"),
                Double(typography, "baseSizePx") ?? 16,
                Widths(Property(document, "contentWidths")),
                MenuColorsOf(Property(Property(document, "menuStyle"), "colors")),
                Property(Property(document, "menuStyle"), "look") is { ValueKind: JsonValueKind.String } look && Looks.Contains(look.GetString()!)
                    ? look.GetString()
                    : null);
        }

        // Only well-formed ids are kept: each becomes a var() name in a <style> element.
        private static Dictionary<string, string> MenuColorsOf(JsonElement? colors) =>
            colors is { ValueKind: JsonValueKind.Object } value
                ? value.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String && TokenId().IsMatch(p.Value.GetString()!))
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
                : [];

        private static IEnumerable<(string, double)> Scale(JsonElement? list) =>
            Items(list)
                .Select(s => (Id(s), Double(s, "rem")))
                .Where(s => s.Item1 is not null && s.Item2 is not null)
                .Select(s => (s.Item1!, s.Item2!.Value));

        private static (double, double, double)? Widths(JsonElement? widths) =>
            Double(widths, "narrow") is { } narrow && Double(widths, "normal") is { } normal && Double(widths, "wide") is { } wide
                ? (narrow, normal, wide)
                : null;

        private static List<JsonElement> Items(JsonElement? list) =>
            list is { ValueKind: JsonValueKind.Array } array ? [.. array.EnumerateArray()] : [];

        private static JsonElement? Property(JsonElement? parent, string name) =>
            parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var child) ? child : null;

        private static string? Id(JsonElement item) =>
            Property(item, "id") is { ValueKind: JsonValueKind.String } id && TokenId().IsMatch(id.GetString()!) ? id.GetString() : null;

        private static string String(JsonElement? parent, string name) =>
            Property(parent, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : string.Empty;

        private static double? Double(JsonElement? parent, string name) =>
            Property(parent, name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

        // A colour goes into a <style> element; anything that could end the value, the rule or
        // the element is refused rather than escaped. The token contract already limits
        // colours to this shape; this is the renderer not trusting that alone (Principle IX).
        [GeneratedRegex("^[#a-zA-Z0-9(),.% -]{1,64}$", RegexOptions.CultureInvariant)]
        private static partial Regex SafeColor();
    }
}

/// <summary>The kinds of design token a property can refer to.</summary>
internal enum TokenKind
{
    Color,
    Space,
    Text,

    /// <summary>A corner radius (066).</summary>
    Radius,
}
