using QuickBuilder.Identity.Contracts;

namespace QuickBuilder.Identity.Infrastructure.Setup;

/// <summary>
/// Setup mode: until the installation is claimed, every admin API request except the setup
/// wizard's own two answers <c>428 identity.setup_required</c> (spec FR-001; research R-12).
/// </summary>
/// <remarks>
/// Only <c>/api</c> is gated. Health probes, the admin shell's static files and the public
/// surface are untouched (FR-006): an upgraded 001 installation keeps serving its sites
/// while setup is pending, and the shell has to load to show the wizard.
/// </remarks>
internal sealed class SetupGateMiddleware(RequestDelegate next)
{
    private static readonly PathString Api = new("/api");

    public async Task InvokeAsync(HttpContext context, IIdentitySetupStatus setupStatus)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(setupStatus);

        if (!context.Request.Path.StartsWithSegments(Api, StringComparison.OrdinalIgnoreCase)
            || IsSetupOperation(context.Request)
            || await setupStatus.IsClaimedAsync(context.RequestAborted).ConfigureAwait(false))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        await IdentityProblems.WriteAsync(context, IdentityErrors.SetupRequired).ConfigureAwait(false);
    }

    private static bool IsSetupOperation(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) && Is(request, "/api/v1/identity/status"))
        || (HttpMethods.IsPost(request.Method) && Is(request, "/api/v1/identity/setup"));

    private static bool Is(HttpRequest request, string path) =>
        string.Equals(request.Path.Value?.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);
}
