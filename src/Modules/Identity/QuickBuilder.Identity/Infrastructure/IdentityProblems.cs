using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>The Identity module's expected failures (contracts/identity-api.yaml).</summary>
/// <remarks>
/// The code is the stable, machine-readable part clients branch on; the message is for
/// people and may change.
/// </remarks>
internal static class IdentityErrors
{
    public static readonly Error SetupRequired = new(
        "identity.setup_required", "This installation has not been set up yet. Complete the setup wizard first.");

    public static readonly Error SetupAlreadyCompleted = new(
        "identity.setup_already_completed", "Setup has already been completed for this installation.");

    public static readonly Error SetupCodeInvalid = new(
        "identity.setup_code_invalid", "That setup code is not correct. It is printed in the application's console output.");

    public static readonly Error Throttled = new(
        "identity.throttled", "Too many attempts. Wait a moment before trying again.");

    public static readonly Error Unauthenticated = new(
        "identity.unauthenticated", "Sign in to continue.");
}

/// <summary>
/// Writes Identity refusals as problem details, in the same shape as every other module's
/// errors (001 FR-009).
/// </summary>
internal static class IdentityProblems
{
    public static int StatusFor(string errorCode) => errorCode switch
    {
        "identity.setup_required" => StatusCodes.Status428PreconditionRequired,
        "identity.setup_already_completed" => StatusCodes.Status409Conflict,
        "identity.setup_code_invalid" => StatusCodes.Status403Forbidden,
        "identity.https_required" => StatusCodes.Status403Forbidden,
        "identity.throttled" => StatusCodes.Status429TooManyRequests,
        "identity.unauthenticated" => StatusCodes.Status401Unauthorized,
        "identity.invalid_credentials" => StatusCodes.Status401Unauthorized,
        "identity.forbidden" => StatusCodes.Status403Forbidden,
        "identity.request_rejected" => StatusCodes.Status403Forbidden,
        "identity.current_password_incorrect" => StatusCodes.Status403Forbidden,
        "identity.email_taken" => StatusCodes.Status409Conflict,
        "identity.last_administrator" => StatusCodes.Status409Conflict,
        "identity.user_deactivated" => StatusCodes.Status409Conflict,
        "identity.user_not_found" => StatusCodes.Status404NotFound,
        "identity.link_invalid" => StatusCodes.Status410Gone,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>For controllers: maps an expected failure to its response.</summary>
    public static IActionResult ToActionResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);
        return new ObjectResult(Create(status, error.Code, TitleFor(status), error.Message))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>For middleware, which has no MVC result pipeline to hand.</summary>
    public static Task WriteAsync(HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = StatusFor(error.Code);
        return WriteAsync(context, status, $"https://quickbuilder.dev/errors/{error.Code.Replace('.', '/')}", TitleFor(status), error.Code, error.Message);
    }

    public static Task WriteAsync(
        HttpContext context,
        int status,
        string type,
        string title,
        string errorCode,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(context);

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

    private static ProblemDetails Create(int status, string errorCode, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://quickbuilder.dev/errors/{errorCode.Replace('.', '/')}",
            Title = title,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();
        return problem;
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => "Sign-in required",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status410Gone => "No longer valid",
        StatusCodes.Status428PreconditionRequired => "Setup required",
        StatusCodes.Status429TooManyRequests => "Too many attempts",
        _ => "The request could not be completed",
    };
}
