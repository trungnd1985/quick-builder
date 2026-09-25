namespace QuickBuilder.Sites.Domain;

/// <summary>
/// Whether a site's host names serve the public (009 research S-10).
/// </summary>
/// <remarks>
/// Text rather than a boolean so the trash-and-restore feature (#21) can add states
/// without renaming a column. The values are stored and appear in the HTTP contract.
/// </remarks>
internal static class SiteStatus
{
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";
}
