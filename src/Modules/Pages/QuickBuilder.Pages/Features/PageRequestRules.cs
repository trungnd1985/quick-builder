using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features;

/// <summary>
/// Request-level checks shared by the slices that take a title and a slug, so every
/// problem is reported at once (001 FR-008). The tree re-checks both; this is only about
/// answering with every field's problem in one response.
/// </summary>
internal static class PageRequestRules
{
    internal static void CheckTitleAndSlug(string? title, string? slug, List<ValidationFailure> failures)
    {
        if (Page.NormaliseTitle(title) is null)
        {
            failures.Add(new ValidationFailure("title", PagesErrors.InvalidTitle.Message));
        }

        if (!PageAddresses.IsValidSlug(slug))
        {
            failures.Add(new ValidationFailure("slug", PagesErrors.InvalidSlug.Message));
        }
    }

    /// <summary>
    /// Reads <c>afterPageId</c>: absent means last, <c>null</c> means first, an id means after
    /// that page (contracts/pages-api.yaml). A value that is not an id is reported as a
    /// missing neighbour, which it is.
    /// </summary>
    internal static Placement ToPlacement(JsonElement afterPageId) => afterPageId.ValueKind switch
    {
        JsonValueKind.Undefined => Placement.Last,
        JsonValueKind.Null => Placement.First,
        JsonValueKind.String when afterPageId.TryGetGuid(out var id) => Placement.After(id),
        _ => Placement.After(Guid.Empty),
    };
}
