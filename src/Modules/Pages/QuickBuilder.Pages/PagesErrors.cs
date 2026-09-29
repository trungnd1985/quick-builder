using System.Runtime.CompilerServices;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages;

/// <summary>
/// Every expected failure the Pages module reports, by code (data-model.md, "Validation and
/// error codes").
/// </summary>
/// <remarks>
/// Some refusals carry more than a code and a message — how many children a page has, how
/// many pages use a component, which rule a document broke and where. <see cref="Error"/>
/// in the kernel has no room for that, and changing it would touch every module for the
/// sake of one, so the extra fields ride alongside: they are attached to the error instance
/// here and read back by <see cref="PagesErrorResults"/>, which copies them into the
/// problem's extensions. A <see cref="ConditionalWeakTable{TKey,TValue}"/> keys on the
/// instance, not on equality, so two equal errors never share fields, and an error nobody
/// holds any more takes its fields with it.
/// </remarks>
internal static class PagesErrors
{
    private static readonly ConditionalWeakTable<Error, IReadOnlyDictionary<string, object?>> ExtensionsByError = [];

    internal static Error OutOfDate { get; } = Error.Conflict(
        "pages.out_of_date",
        "This was changed by someone else since you opened it. Reload and try again.");

    internal static Error NotFound { get; } = Error.NotFound("pages.not_found", "No such page on this site.");

    internal static Error ComponentNotFound { get; } =
        Error.NotFound("pages.component_not_found", "No such component on this site.");

    internal static Error SiteNotFound { get; } = Error.NotFound("sites.not_found", "No such site.");

    internal static Error ParentNotFound { get; } =
        Error.NotFound("pages.parent_not_found", "The parent or neighbouring page is not a page of this site.");

    internal static Error InvalidTitle { get; } = Error.Validation(
        "pages.invalid_title",
        $"A page title must be between 1 and {Domain.Page.MaxTitleLength} characters.");

    internal static Error InvalidSlug { get; } = Error.Validation(
        "pages.invalid_slug",
        $"A slug is 1 to {PageAddresses.MaxSlugLength} lowercase letters, digits and single hyphens, not starting or ending with a hyphen.");

    internal static Error TooManyPages { get; } = Error.Conflict(
        "pages.too_many_pages",
        $"A site can hold at most {PageAddresses.MaxPagesPerSite} pages.");

    internal static Error MoveIntoDescendant { get; } = Error.Conflict(
        "pages.move_into_descendant",
        "A page cannot be moved under itself or one of its own pages.");

    internal static Error HomePageExists { get; } = Error.Conflict(
        "pages.home_page_exists",
        "This site already has a home page. Make another page the home page instead.");

    internal static Error HomePageMustBeTopLevel { get; } = Error.Conflict(
        "pages.home_page_must_be_top_level",
        "The home page must be a top-level page.");

    internal static Error HomePage { get; } = Error.Conflict(
        "pages.home_page",
        "The home page cannot be deleted while the site has other pages. Make another page the home page first.");

    internal static Error UnknownComponent { get; } = Error.Validation(
        "pages.unknown_component",
        "The document refers to a component that is not a component of this site.");

    internal static Error InvalidComponentName { get; } = Error.Validation(
        "pages.invalid_component_name",
        $"A component name must be between 1 and {Domain.Component.MaxNameLength} characters.");

    internal static Error DuplicateComponentName { get; } = Error.Conflict(
        "pages.duplicate_component_name",
        "This site already has a component with that name.");

    internal static Error InvalidTokenId { get; } = Error.Validation(
        "pages.invalid_token_id",
        "A design token id is 1 to 40 lowercase letters, digits and hyphens.");

    internal static Error DocumentTooLarge { get; } = Error.Validation(
        "pages.document_too_large",
        $"A document may be at most {PageSchema.MaxDocumentBytes} bytes.");

    internal static Error DuplicateAddress(string path) => Error.Conflict(
        "pages.duplicate_address",
        $"Another page already has the address {path}.");

    internal static Error ReservedAddress(string path) => Error.Conflict(
        "pages.reserved_address",
        $"The address {path} is reserved by the platform.");

    internal static Error AddressTooLong(string path) => Error.Conflict(
        "pages.address_too_long",
        $"The address {Truncate(path)} would be longer than {PageAddresses.MaxPathLength} characters.");

    internal static Error TooDeep() => Error.Conflict(
        "pages.too_deep",
        $"Pages can be nested at most {PageAddresses.MaxDepth} levels deep.");

    internal static Error HasChildren(int childCount) => With(
        Error.Conflict(
            "pages.has_children",
            $"This page has {childCount} child page(s). Move or delete them first."),
        ("childCount", childCount));

    internal static Error ComponentInUse(int usageCount) => With(
        Error.Conflict(
            "pages.component_in_use",
            $"This component is used by {usageCount} page(s). Remove it from them first."),
        ("usageCount", usageCount));

    // --- Publishing (012 data-model §6) ---------------------------------------------------

    internal static Error PagePublished { get; } = Error.Conflict(
        "pages.page_published",
        "This page is published. Unpublish it before deleting it.");

    internal static Error NotPublished { get; } = Error.Conflict(
        "pages.not_published",
        "This is not published.");

    internal static Error VersionNotFound { get; } = Error.NotFound(
        "pages.version_not_found",
        "No such version is kept for this page or component.");

    internal static Error ScheduleInPast { get; } = Error.Validation(
        "pages.schedule_in_past",
        "Choose a time in the future.");

    internal static Error ScheduleNotFound { get; } = Error.NotFound(
        "pages.schedule_not_found",
        "Nothing is scheduled.");

    internal static Error InvalidCursor { get; } = Error.Validation(
        "pages.invalid_cursor",
        "The cursor is not one this server issued.");

    internal static Error InvalidLimit(int maximum) => Error.Validation(
        "pages.invalid_limit",
        $"The page size must be between 1 and {maximum}.");

    internal static Error UnpublishedComponents(IReadOnlyCollection<Guid> componentIds) => With(
        Error.Conflict(
            "pages.unpublished_components",
            $"{componentIds.Count} component(s) this uses are not published. Publish them first."),
        ("componentIds", componentIds.ToArray()));

    internal static Error ComponentInUseByPublishedPages(int pageCount) => With(
        Error.Conflict(
            "pages.component_in_use_by_published_pages",
            $"This component is used by {pageCount} published page(s). Unpublish them or remove it from them first."),
        ("pageCount", pageCount));

    internal static Error VersionMissingComponents(IReadOnlyCollection<Guid> componentIds) => With(
        Error.Conflict(
            "pages.version_missing_components",
            $"This version uses {componentIds.Count} component(s) that no longer exist."),
        ("componentIds", componentIds.ToArray()));

    /// <summary>A document refusal, carrying the failed rule and where it failed.</summary>
    internal static Error InvalidDocument(DocumentValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var error = result.Code switch
        {
            "pages.document_too_large" => Error.Validation(result.Code, result.Message ?? DocumentTooLarge.Message),
            null => throw new ArgumentException("A valid document is not an error.", nameof(result)),
            _ => Error.Validation(result.Code, result.Message ?? "The document does not follow the page schema."),
        };

        return With(error, ("rule", result.Rule), ("pointer", result.Location));
    }

    /// <summary>The extra problem fields attached to <paramref name="error"/>, if any.</summary>
    internal static IReadOnlyDictionary<string, object?>? ExtensionsOf(Error error) =>
        ExtensionsByError.TryGetValue(error, out var extensions) ? extensions : null;

    private static Error With(Error error, params (string Name, object? Value)[] fields)
    {
        ExtensionsByError.AddOrUpdate(error, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));
        return error;
    }

    // The address itself is fine to show — it is the user's own input — but not 1,024
    // characters of it.
    private static string Truncate(string path) => path.Length <= 80 ? path : string.Concat(path.AsSpan(0, 77), "...");
}
