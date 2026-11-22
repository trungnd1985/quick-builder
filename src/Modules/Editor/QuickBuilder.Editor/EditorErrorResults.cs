using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor;

/// <summary>
/// Maps an expected failure to an HTTP response (001 FR-009, 011 data-model §6).
/// </summary>
/// <remarks>
/// The code is the stable part of the contract — clients branch on it — so which status it
/// gets is decided here, once, in the same problem shape Pages uses.
/// </remarks>
internal static class EditorErrorResults
{
    internal static IActionResult ToActionResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);

        var problem = new ProblemDetails
        {
            Type = $"https://quickbuilder.dev/errors/{error.Code.Replace('.', '/')}",
            Title = TitleFor(status),
            Status = status,
            Detail = error.Message,
        };
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString();

        if (EditorErrors.ExtensionsOf(error) is { } extensions)
        {
            foreach (var (name, value) in extensions)
            {
                problem.Extensions[name] = value;
            }
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    internal static int StatusFor(string code) => code switch
    {
        "editor.out_of_date" or "editor.document_too_new" => StatusCodes.Status409Conflict,
        "editing.lock_held" or "editing.not_stale" => StatusCodes.Status409Conflict,
        "editing.session_gone" => StatusCodes.Status404NotFound,
        "editing.forbidden" => StatusCodes.Status403Forbidden,
        "editor.document_upgrade_failed" => StatusCodes.Status422UnprocessableEntity,
        "editor.tokens_too_large" => StatusCodes.Status413PayloadTooLarge,

        // 022 data-model §5.
        "preview.link_limit" or "preview.no_template" => StatusCodes.Status409Conflict,
        "preview.version_not_retained" => StatusCodes.Status404NotFound,
        _ when code.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Too large",
        _ => "The request could not be completed",
    };
}
