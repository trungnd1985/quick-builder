namespace QuickBuilder.Rendering.Contracts;

/// <summary>Public-surface paths the Rendering module answers besides pages (research PR-08).</summary>
public static class RenderingRoutes
{
    /// <summary>
    /// Where the block stylesheet is served (public-rendering.md §5). The slug grammar cannot
    /// produce a leading <c>_</c>, so no page can ever claim it.
    /// </summary>
    public const string AssetsPrefix = "/_assets";
}
