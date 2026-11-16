using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages;

/// <summary>
/// Maps an expected failure to an HTTP response (001 FR-009, data-model.md error codes).
/// </summary>
/// <remarks>
/// The code is the stable part of the contract — clients branch on it — so which status it
/// gets is decided here, once. Fields some refusals carry (<c>childCount</c>,
/// <c>usageCount</c>, <c>rule</c>, <c>pointer</c>) come from <see cref="PagesErrors"/> and
/// go into the problem's extensions.
/// </remarks>
internal static class PagesErrorResults
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

        if (PagesErrors.ExtensionsOf(error) is { } extensions)
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

    internal static int StatusFor(string code) => QuickBuilder.Shared.Infrastructure.Trash.TrashErrorCodes.StatusFor(code) ?? code switch
    {
        "pages.duplicate_address"
            or "pages.reserved_address"
            or "pages.address_too_long"
            or "pages.too_deep"
            or "pages.too_many_pages"
            or "pages.move_into_descendant"
            or "pages.home_page_exists"
            or "pages.home_page_must_be_top_level"
            or "pages.has_children"
            or "pages.home_page"
            or "pages.out_of_date"
            or "editing.lock_held"
            or "pages.admin_outdated"
            or "pages.duplicate_component_name"
            or "pages.component_in_use"
            // 012 data-model §6.
            or "pages.page_published"
            or "pages.not_published"
            or "pages.unpublished_components"
            or "pages.component_in_use_by_published_pages"
            or "pages.version_missing_components"
            // 016 data-model §4.
            or "pages.template_has_children"
            or "pages.collection_has_template"
            or "pages.template_parent_taken"
            // 017 data-model §4.
            or "redirects.duplicate"
            or "redirects.loop"
            or "redirects.limit_reached"
            // 024 data-model §4.2.
            or "pages.duplicate_menu_name"
            or "pages.menu_limit_reached"
            or "pages.unpublished_menus"
            or "pages.version_missing_menus"
            or "pages.menu_in_use_by_published_content"
            // 018 form-block.md rule 28.
            or "pages.form_invalid"
            or "pages.form_redirect_not_live"
            // 019 contracts/schema-upgrades.md §3.
            or "pages.document_too_new" => StatusCodes.Status409Conflict,
        "pages.seo_restricted" or "identity.forbidden" => StatusCodes.Status403Forbidden,
        "redirects.import_too_large" => StatusCodes.Status413PayloadTooLarge,
        "pages.document_too_large" => StatusCodes.Status413PayloadTooLarge,
        "pages.image_alt_missing"
            or "pages.document_upgrade_failed" => StatusCodes.Status422UnprocessableEntity,
        _ when code.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Too large",
        _ => "The request could not be completed",
    };
}
