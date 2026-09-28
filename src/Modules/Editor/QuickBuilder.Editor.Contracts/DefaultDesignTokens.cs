using System.Text.Json;

namespace QuickBuilder.Editor.Contracts;

/// <summary>
/// The token set a site uses until it saves its own (research ED-14, 011 FR-032).
/// </summary>
/// <remarks>
/// Embedded from <c>docs/contracts/design-tokens/examples/valid/default.json</c>, the
/// contract's own golden file, which the admin app imports too. So the server, the editor and
/// the contract share one copy. Its ids are fixed: the page schema's property defaults name
/// them (<c>color-text</c>, <c>space-md</c> and so on).
/// </remarks>
public static class DefaultDesignTokens
{
    private const string ResourceName = "QuickBuilder.Editor.Contracts.DesignTokens.default.json";

    private static readonly Lazy<string> Text = new(() =>
    {
        using var stream = typeof(DefaultDesignTokens).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "The default design tokens are not embedded. Check the EmbeddedResource in QuickBuilder.Editor.Contracts.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>The default set as JSON text.</summary>
    public static string Json => Text.Value;

    /// <summary>The default set, parsed. Each call returns a detached copy.</summary>
    public static JsonElement Document
    {
        get
        {
            using var document = JsonDocument.Parse(Json);
            return document.RootElement.Clone();
        }
    }
}
