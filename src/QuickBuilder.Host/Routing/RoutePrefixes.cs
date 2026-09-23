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
/// </remarks>
public static class RoutePrefixes
{
    public const string Api = "/api";
    public const string Admin = "/admin";
    public const string Health = "/health";

    private static readonly string[] Reserved = [Api, Admin, Health];

    /// <summary>True when the path belongs to the administrative surface.</summary>
    public static bool IsReserved(PathString path) =>
        Array.Exists(Reserved, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The reserved prefixes, for tests and documentation.</summary>
    public static IReadOnlyList<string> ReservedPrefixes => Reserved;
}
