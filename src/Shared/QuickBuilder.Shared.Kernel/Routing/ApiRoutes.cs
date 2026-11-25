namespace QuickBuilder.Shared.Kernel.Routing;

/// <summary>Where the host serves the administrative API (FR-010a), under <c>Hosting:BasePath</c>.</summary>
/// <remarks>One spelling for the host's routing and every module that matches it (#58).</remarks>
public static class ApiRoutes
{
    /// <summary>The API's root, relative to the base path, without a trailing slash.</summary>
    public const string Root = "/api";
}
