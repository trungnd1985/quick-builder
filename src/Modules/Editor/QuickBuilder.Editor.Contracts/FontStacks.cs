namespace QuickBuilder.Editor.Contracts;

/// <summary>
/// The fonts a token set may name: system font stacks from the Modern Font Stacks project
/// (CC0), so nothing is ever downloaded (research ED-17, constitution Principle II).
/// </summary>
/// <remarks>
/// Part of the design token contract (docs/contracts/design-tokens.md §3).
/// <c>FontStackParityTests</c> fails the build if this list, the document's table, the schema's
/// enum or the admin app's copy disagree. Adding a stack is additive; removing one is breaking,
/// because stored token sets name it.
/// </remarks>
public static class FontStacks
{
    /// <summary>Each stack id with its CSS <c>font-family</c>, in the contract's order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> All { get; } =
    [
        new("system-ui", "system-ui, sans-serif"),
        new("transitional", "Charter, 'Bitstream Charter', 'Sitka Text', Cambria, serif"),
        new("old-style", "'Iowan Old Style', 'Palatino Linotype', 'URW Palladio L', P052, serif"),
        new("humanist", "Seravek, 'Gill Sans Nova', Ubuntu, Calibri, 'DejaVu Sans', source-sans-pro, sans-serif"),
        new("geometric-humanist", "Avenir, Montserrat, Corbel, 'URW Gothic', source-sans-pro, sans-serif"),
        new("classical-humanist", "Optima, Candara, 'Noto Sans', source-sans-pro, sans-serif"),
        new("neo-grotesque", "Inter, Roboto, 'Helvetica Neue', 'Arial Nova', 'Nimbus Sans', Arial, sans-serif"),
        new("monospace-slab", "'Nimbus Mono PS', 'Courier New', monospace"),
        new("monospace-code", "ui-monospace, 'Cascadia Code', 'Source Code Pro', Menlo, Consolas, 'DejaVu Sans Mono', monospace"),
        new("industrial", "Bahnschrift, 'DIN Alternate', 'Franklin Gothic Medium', 'Nimbus Sans Narrow', sans-serif-condensed, sans-serif"),
        new("rounded-sans", "ui-rounded, 'Hiragino Maru Gothic ProN', Quicksand, Comfortaa, Manjari, 'Arial Rounded MT', 'Arial Rounded MT Bold', Calibri, source-sans-pro, sans-serif"),
        new("slab-serif", "Rockwell, 'Rockwell Nova', 'Roboto Slab', 'DejaVu Serif', 'Sitka Small', serif"),
        new("antique", "Superclarendon, 'Bookman Old Style', 'URW Bookman', 'URW Bookman L', 'Georgia Pro', Georgia, serif"),
        new("didone", "Didot, 'Bodoni MT', 'Noto Serif Display', 'URW Palladio L', P052, Sylfaen, serif"),
        new("handwritten", "'Segoe Print', 'Bradley Hand', Chilanka, TSCu_Comic, casual, cursive"),
    ];

    /// <summary>The CSS <c>font-family</c> for <paramref name="id"/>, or null for an unknown id.</summary>
    public static string? FamilyOf(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Key, id, StringComparison.Ordinal)).Value;
}
