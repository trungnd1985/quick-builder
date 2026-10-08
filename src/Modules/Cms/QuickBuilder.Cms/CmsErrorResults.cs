using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms;

/// <summary>
/// Maps an expected failure to an HTTP response (001 FR-009, 014 data-model §5).
/// </summary>
/// <remarks>
/// The code is the stable part of the contract (clients branch on it), so which status it
/// gets is decided here, once. Extra fields come from <see cref="CmsErrors"/>.
/// </remarks>
internal static class CmsErrorResults
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

        if (CmsErrors.ExtensionsOf(error) is { } extensions)
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
        "cms.slug_taken"
            or "cms.too_many_collections"
            or "cms.revision_conflict"
            or "cms.collection_busy"
            or "cms.confirmation_required"
            or "cms.preview_outdated"
            or "cms.collection_referenced"
            or "cms.items_being_updated"
            or "cms.item_slug_taken"
            or "cms.item_conflict"
            or "cms.admin_outdated"
            or "cms.schema_changed"
            or "cms.item_needs_attention"
            or "cms.version_not_publishable"
            or "cms.item_not_published"
            or "cms.item_referenced"
            or "cms.schedule_pending" => StatusCodes.Status409Conflict,
        "cms.proposal_too_large" or "cms.item_too_large" => StatusCodes.Status413PayloadTooLarge,
        _ when code.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Too large",
        _ => "The request could not be completed",
    };
}
