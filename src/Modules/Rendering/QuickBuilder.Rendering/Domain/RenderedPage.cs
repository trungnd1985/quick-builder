namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// A page as visitors get it: UTF-8 HTML and the hash of its one style element, for its
/// content security policy (012 data-model §5). What the output cache holds.
/// </summary>
internal sealed record RenderedPage(byte[] Html, string StyleHash);
