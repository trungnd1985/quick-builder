namespace QuickBuilder.Host.Routing;

/// <summary>
/// The administrative / public surface split (FR-011).
/// </summary>
/// <remarks>
/// FR-011 requires this split to be explicit and documented rather than implied by
/// routing order. This type is that explicit statement: reserved prefixes are matched
/// before the public catch-all, and a routing test asserts the order rather than leaving
/// it to registration sequence.
/// <para>
/// <b>Administrative surface</b> — <c>/api</c>, <c>/admin</c>, <c>/health</c>. Never
/// requires a resolved site. Works on an installation with zero sites. Where an operation
/// concerns one site, the site is named explicitly in the request (FR-011b).
/// </para>
/// <para>
/// <b>Public surface</b> — everything else. Always requires a site, resolved from the
/// Host header. A request that resolves to nothing is rejected (FR-014).
/// </para>
/// <para>
/// <b>Reserved on the public surface</b> — <c>/_media</c> (003 FR-015). Still public and
/// still site-resolved: a media file is served only on its own site's host names. It is
/// reserved in the other sense: no page may ever claim a path under it, so the Pages
/// feature must refuse slugs beginning with <c>_media</c>.
/// </para>
/// </remarks>
public static class RoutePrefixes
{
    public const string Api = "/api";
    public const string Admin = "/admin";
    public const string Health = "/health";

    /// <summary>Where each site serves its media files (003 research M-11).</summary>
    public const string PublicMedia = "/_media";

    private static readonly string[] Reserved = [Api, Admin, Health];

    /// <summary>Where the block stylesheet is served (012 research PR-08).</summary>
    public const string PublicAssets = QuickBuilder.Rendering.Contracts.RenderingRoutes.AssetsPrefix;

    /// <summary>
    /// Where forms post their submissions (018 research FS-06). Site-resolved like
    /// <c>/_media</c>: a form is accepted only on its own site's host names.
    /// </summary>
    public const string PublicForms = QuickBuilder.Forms.Contracts.FormsRoutes.SubmitPrefix;

    private static readonly string[] PublicReserved = [PublicMedia, PublicAssets, PublicForms];

    /// <summary>
    /// Where previews of drafts and versions are served (022 research PV-05). Answers on every
    /// host name; the site comes from the preview link or the route, never from <c>Host</c>.
    /// Never logged: its paths may carry a link secret, and they are not in
    /// <see cref="IsReserved"/>, so the log redactor removes them.
    /// </summary>
    public const string Preview = "/_preview";

    private static readonly string[] UnresolvedPublic = [Preview];

    /// <summary>
    /// Public prefixes that site resolution skips: they answer on every host name and name their
    /// site themselves (022 research PV-05).
    /// </summary>
    public static IReadOnlyList<string> UnresolvedPublicPrefixes => UnresolvedPublic;

    /// <summary>True when the path belongs to <see cref="UnresolvedPublicPrefixes"/>.</summary>
    public static bool IsUnresolvedPublic(PathString path) =>
        Array.Exists(UnresolvedPublic, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the path belongs to the administrative surface.</summary>
    public static bool IsReserved(PathString path) =>
        Array.Exists(Reserved, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The reserved prefixes, for tests and documentation.</summary>
    public static IReadOnlyList<string> ReservedPrefixes => Reserved;

    /// <summary>
    /// Prefixes on the public surface that belong to the platform, not to pages. Site
    /// resolution still applies to them.
    /// </summary>
    public static IReadOnlyList<string> PublicReservedPrefixes => PublicReserved;
}
