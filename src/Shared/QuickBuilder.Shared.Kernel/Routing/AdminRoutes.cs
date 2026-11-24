namespace QuickBuilder.Shared.Kernel.Routing;

/// <summary>
/// Where the host serves the admin app (FR-043), under <c>Hosting:BasePath</c> when one is set.
/// </summary>
/// <remarks>
/// One spelling for the host's route and for every link a module builds to the admin
/// (notification emails, the <c>identity</c> commands, the preview member bar), so a link
/// cannot drift from where the admin actually is (#50, #53). Modules cannot reference the
/// host, so the value lives here and the host's <c>RoutePrefixes.Admin</c> is defined from it.
/// </remarks>
public static class AdminRoutes
{
    /// <summary>The admin app's root, relative to the base path, without a trailing slash.</summary>
    public const string Root = "/admin";
}
