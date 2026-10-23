namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// A page as visitors get it: UTF-8 HTML and the hash of its one style element, for its
/// content security policy (012 data-model §5). What the output cache holds.
/// </summary>
/// <param name="ScriptHash">
/// The hash of the navigation script when the page carries it, for <c>script-src</c> (024 NV-14);
/// null for a page without a Navigation block.
/// </param>
/// <param name="FormScriptHash">
/// The hash of the form script when the page has a form (018 research FS-10); null otherwise.
/// A page with neither script admits no script at all.
/// </param>
/// <param name="FormActionOrigins">
/// The external origins forms on the page redirect to after a submission, for
/// <c>form-action</c> (018 research FS-11); empty for most pages.
/// </param>
internal sealed record RenderedPage(
    byte[] Html,
    string StyleHash,
    string? ScriptHash = null,
    string? FormScriptHash = null,
    IReadOnlyList<string>? FormActionOrigins = null)
{
    /// <summary>Whether the page holds a form, whose policy lets it post and fetch to the site.</summary>
    public bool HasForm => FormScriptHash is not null;
}
