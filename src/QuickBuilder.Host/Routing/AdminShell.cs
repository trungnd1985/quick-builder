using System.Text.Encodings.Web;

namespace QuickBuilder.Host.Routing;

/// <summary>
/// Serves the admin app's shell, <c>wwwroot/admin/index.html</c>, for any <c>/admin/*</c>
/// address the app routes itself (FR-043), with its own addresses made absolute (#51).
/// </summary>
/// <remarks>
/// The admin is built once and served under whatever <c>Hosting:BasePath</c> the operator
/// sets, so the build writes relative addresses (<c>./assets/...</c>). Those are right only
/// at <c>/admin/</c> itself: a deep link such as <c>/admin/sites/1/pages</c> would resolve
/// them under <c>/admin/sites/1/</c>. The shell is the one file that needs fixing up; the
/// scripts and stylesheets it loads resolve their own addresses against their own location.
/// A <c>&lt;base&gt;</c> element would do the same but also move every <c>href="#..."</c>
/// fragment link, such as the skip link, off the page.
/// </remarks>
internal static class AdminShell
{
    internal const string File = "admin/index.html";

    /// <summary>The terminal handler for the admin's SPA fallback route.</summary>
    internal static async Task ServeAsync(HttpContext context)
    {
        var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var file = environment.WebRootFileProvider.GetFileInfo(File);
        if (!file.Exists)
        {
            // As MapFallbackToFile answered: the admin app was not built into wwwroot.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string shell;
        var stream = file.CreateReadStream();
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            shell = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response
            .WriteAsync(Rewrite(shell, context.Request.PathBase), context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns the shell's relative attribute values (<c>="./...</c>) into absolute ones under
    /// <paramref name="pathBase"/> and <see cref="RoutePrefixes.Admin"/>.
    /// </summary>
    internal static string Rewrite(string shell, PathString pathBase)
    {
        var root = HtmlEncoder.Default.Encode(pathBase.Add(RoutePrefixes.Admin).ToUriComponent());
        return shell.Replace("=\"./", $"=\"{root}/", StringComparison.Ordinal);
    }
}
