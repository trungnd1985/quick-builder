using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Sites;

/// <summary>
/// Maps an expected failure to an HTTP response (FR-009).
/// </summary>
/// <remarks>
/// The whole point of <see cref="Result"/> carrying an <see cref="Error"/> is that a
/// controller can do this without catching anything. The error code is the stable part of
/// the contract — clients branch on it — so the mapping from code to status lives here,
/// in one place, rather than being re-decided in every controller.
/// </remarks>
internal static class ErrorResults
{
    internal static IActionResult ToActionResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Code switch
        {
            "sites.host_name_taken"
                or "sites.primary_host_name_removal"
                or "sites.last_host_name"
                or "sites.out_of_date" => StatusCodes.Status409Conflict,
            "sites.not_found" => StatusCodes.Status404NotFound,
            _ when error.Code.EndsWith("_not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest,
        };

        var problem = new ProblemDetails
        {
            Type = $"https://quickbuilder.dev/errors/{error.Code.Replace('.', '/')}",
            Title = TitleFor(status),
            Status = status,
            Detail = error.Message,
        };
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString();

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "The request could not be completed",
    };
}
