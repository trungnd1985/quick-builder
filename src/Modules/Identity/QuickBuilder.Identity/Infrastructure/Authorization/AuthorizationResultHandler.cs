using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace QuickBuilder.Identity.Infrastructure.Authorization;

/// <summary>
/// Writes every authorization refusal as problem details (contracts/authorization.md §3).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>No valid session: 401 <c>identity.unauthenticated</c>.</item>
/// <item>Signed in, cannot see the site named in the route: 404 byte-identical (apart from
/// <c>traceId</c>) to the Sites module's "no such site", so the response reveals nothing
/// about whether the site exists (FR-022).</item>
/// <item>Otherwise: 403 <c>identity.forbidden</c> (FR-023).</item>
/// </list>
/// </remarks>
internal sealed class AuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "https://quickbuilder.dev/errors/identity/unauthenticated",
                "Sign-in required",
                "identity.unauthenticated",
                "Sign in to continue.").ConfigureAwait(false);
            return;
        }

        var siteNotVisible = authorizeResult.AuthorizationFailure?.FailureReasons
            .Any(r => r.Message == CapabilityRequirementHandler.SiteNotVisible) == true;

        if (siteNotVisible)
        {
            // Must match QuickBuilder.Sites ErrorResults for "sites.not_found" exactly.
            await WriteAsync(
                context,
                StatusCodes.Status404NotFound,
                "https://quickbuilder.dev/errors/sites/not_found",
                "Not found",
                "sites.not_found",
                "No such site.").ConfigureAwait(false);
            return;
        }

        await WriteAsync(
            context,
            StatusCodes.Status403Forbidden,
            "https://quickbuilder.dev/errors/identity/forbidden",
            "Forbidden",
            "identity.forbidden",
            "You do not have permission to do this.").ConfigureAwait(false);
    }

    internal static Task WriteAsync(
        HttpContext context,
        int status,
        string type,
        string title,
        string errorCode,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", context.RequestAborted);
    }
}
