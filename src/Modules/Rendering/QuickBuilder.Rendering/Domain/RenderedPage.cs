namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// A page as visitors get it: UTF-8 HTML and the hash of its one style element, for its
/// content security policy (012 data-model §5). What the output cache holds.
/// </summary>
/// <param name="ScriptHash">
/// The hash of the navigation script when the page carries it, for <c>script-src</c> (024 NV-14);
/// null for a page without a Navigation block, whose policy admits no script at all.
/// </param>
internal sealed record RenderedPage(byte[] Html, string StyleHash, string? ScriptHash = null);
