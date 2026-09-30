namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// A request path as a page address (012 public-rendering.md §2).
/// </summary>
/// <remarks>
/// The server has already removed the base path and decoded the path once. What is left is
/// checked, not repaired: dot segments, empty segments, backslashes, control characters and
/// anything still percent-encoded mean no page, never a guess. Addresses are unique ignoring
/// case and stored lower-case (010), so the path is lower-cased; one trailing slash is ignored.
/// </remarks>
internal static class PathNormaliser
{
    /// <summary>The address to look up, or null when no page can have it.</summary>
    public static string? Normalise(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }

        if (path[0] != '/' || path.Length > 1100)
        {
            return null;
        }

        var trimmed = path.Length > 1 && path[^1] == '/' ? path[..^1] : path;

        foreach (var c in trimmed)
        {
            if (char.IsControl(c) || c is '\\' or '%')
            {
                return null;
            }
        }

        foreach (var segment in trimmed[1..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return null;
            }
        }

        return trimmed.ToLowerInvariant();
    }
}
