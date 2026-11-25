namespace QuickBuilder.Shared.Kernel.Routing;

/// <summary>
/// The platform's own prefixes on the public surface (FR-011): every site's host names answer
/// them, and no page may claim an address under them.
/// </summary>
/// <remarks>
/// One spelling each for the module that serves the route, the modules that write addresses
/// under it, and the host that reserves it (#58). They live here rather than in the serving
/// module's Contracts because modules that may not reference that module still write its
/// addresses: Rendering writes the form action, but nothing may reference Forms.
/// </remarks>
public static class PublicRoutes
{
    /// <summary>Uploaded files (003 research M-11).</summary>
    public const string Media = "/_media";

    /// <summary>The block stylesheet (012 research PR-08).</summary>
    public const string Assets = "/_assets";

    /// <summary>Form submissions (018 research FS-06).</summary>
    public const string Forms = "/_forms";

    /// <summary>Previews of drafts and versions (022 research PV-05).</summary>
    public const string Preview = "/_preview";
}
