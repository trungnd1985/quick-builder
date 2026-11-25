namespace QuickBuilder.Shared.Kernel.Routing;

/// <summary>Where the host answers its health probes, under <c>Hosting:BasePath</c>.</summary>
/// <remarks>One spelling for the probes, the refusal host and telemetry's filter (#58).</remarks>
public static class HealthRoutes
{
    /// <summary>The probes' root, relative to the base path, without a trailing slash.</summary>
    public const string Root = "/health";
}
