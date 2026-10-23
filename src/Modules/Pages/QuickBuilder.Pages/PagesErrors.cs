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

    /// <summary>035 FR-020: an admin from before 035 would drop rich text formats it does not know.</summary>
    internal static Error AdminOutdated { get; } = Error.Conflict(
        "pages.admin_outdated",
        "This page of the admin is out of date. Reload it to keep editing; your last change was not saved.");

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

    // Templates (016 data-model §4).

    internal static Error TemplateNeedsParent { get; } = Error.Validation(
        "pages.template_needs_parent",
        "A template needs a parent page other than the home page, so its items' addresses start with that page's address.");

    internal static Error TemplateHasChildren { get; } = Error.Conflict(
        "pages.template_has_children",
        "A template cannot have pages under it, because its address holds the item's slug.");

    internal static Error UnknownCollection { get; } = Error.Validation(
        "pages.unknown_collection",
        "That is not a collection of this site.");

    internal static Error CollectionHasTemplate(Guid pageId) => With(
        Error.Conflict("pages.collection_has_template", "That collection already has a template page. A collection has one template."),
        ("pageId", pageId));

    internal static Error TemplateParentTaken { get; } = Error.Conflict(
        "pages.template_parent_taken",
        "That page already holds a template. A page holds at most one.");

    // Search metadata and redirects (017 data-model §4).

    /// <summary>SE-03: canonical address and hiding are page structure decisions.</summary>
    internal static Error SeoRestricted(string field) => With(
        Error.Validation(
            "pages.seo_restricted",
            "Only members who may change the page structure can change the canonical address or hide a page from search engines."),
        ("field", field));

    internal static Error RedirectNotFound { get; } = Error.NotFound("redirects.not_found", "No such redirect on this site.");

    internal static Error RedirectSourceInvalid(string reason) => Error.Validation(
        "redirects.source_invalid",
        $"That source address cannot be redirected: {reason}");

    internal static Error RedirectTargetInvalid(string reason) => Error.Validation(
        "redirects.target_invalid",
        $"That target cannot be used: {reason}");

    internal static Error RedirectDuplicate(Guid redirectId) => With(
        Error.Conflict("redirects.duplicate", "Another redirect already has that source address."),
        ("redirectId", redirectId));

    internal static Error RedirectLoop(Guid? redirectId) => With(
        Error.Conflict("redirects.loop", "This redirect would send visitors round in a loop."),
        ("redirectId", redirectId));

    internal static Error RedirectLimitReached(int limit) => With(
        Error.Conflict("redirects.limit_reached", $"A site may have at most {limit} redirects."),
        ("limit", limit));

    internal static Error RedirectImportEncoding { get; } = Error.Validation(
        "redirects.import_encoding",
        "The file is not UTF-8 text. Save it as UTF-8 and import it again.");

    internal static Error RedirectImportTooLarge(int maxRows, int maxBytes) => Error.Validation(
        "redirects.import_too_large",
        $"An import may hold at most {maxRows} rows and {maxBytes / (1024 * 1024)} MB.");

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

    internal static Error DraftVersionRequired { get; } = Error.Validation(
        "pages.draft_version_required",
        "A scheduled publish needs the draft version that was reviewed.");

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

    /// <summary>
    /// 035 FR-014: rich text images that need alternative text, or to be marked decorative,
    /// before publishing; <c>problems</c> gives each one's JSON pointer.
    /// </summary>
    internal static Error ImageAltMissing(IReadOnlyCollection<string> pointers) => With(
        Error.Validation(
            "pages.image_alt_missing",
            $"{pointers.Count} image(s) in rich text need alternative text, or to be marked decorative, before this is published."),
        ("problems", pointers.Select(p => new Dictionary<string, string> { ["pointer"] = p }).ToArray()));

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

    /// <summary>
    /// 016 FR-027, contract rule 21: a paginated list reads the same page parameter as a list of
    /// a page or component shown with it. <c>with</c> names that page or component.
    /// </summary>
    internal static Error PageParamTaken(string param, string pointer, string with) => With(
        Error.Validation(
            "pages.invalid_document",
            $"A paginated list here uses the page parameter \"{param}\", like a list in \"{with}\" shown with it. Give one of them its own name."),
        ("rule", Infrastructure.PageParamRules.Rule),
        ("pointer", pointer),
        ("param", param),
        ("with", with));

    // --- Menus (024 data-model §4.2) ---------------------------------------------------------

    internal static Error MenuNotFound { get; } = Error.NotFound("pages.menu_not_found", "No such menu on this site.");

    internal static Error InvalidMenuName { get; } = Error.Validation(
        "pages.invalid_menu_name",
        $"A menu name must be between 1 and {Domain.Menus.Menu.MaxNameLength} characters.");

    internal static Error DuplicateMenuName { get; } = Error.Conflict(
        "pages.duplicate_menu_name",
        "This site already has a menu with that name.");

    internal static Error MenuLimitReached { get; } = With(
        Error.Conflict(
            "pages.menu_limit_reached",
            $"A site may have at most {Domain.Menus.Menu.MaxPerSite} menus."),
        ("limit", Domain.Menus.Menu.MaxPerSite));

    internal static Error InvalidReferenceQuery { get; } = Error.Validation(
        "pages.invalid_reference_query",
        "Ask about exactly one page or one item.");

    /// <summary>FR-003a: a Navigation block names menus that do not exist or are not live.</summary>
    internal static Error UnpublishedMenus(IReadOnlyCollection<Guid> menuIds) => With(
        Error.Conflict(
            "pages.unpublished_menus",
            $"{menuIds.Count} menu(s) this shows are not published. Publish them first."),
        ("menuIds", menuIds.ToArray()));

    /// <summary>
    /// 018 rule 28.1–28.5: a form is not ready for visitors. Names the form, the field and the
    /// first broken rule; the editor shows every problem.
    /// </summary>
    internal static Error FormInvalid(string formId, string? fieldId, string rule) => With(
        Error.Conflict("pages.form_invalid", "A form on this is not complete. Fix it in the editor, then publish."),
        ("formId", formId),
        ("fieldId", fieldId),
        ("rule", rule));

    /// <summary>018 rule 28.6: a form redirects to, or its consent links to, a page that is not live.</summary>
    internal static Error FormRedirectNotLive(string formId, Guid pageId) => With(
        Error.Conflict("pages.form_redirect_not_live", "A form on this points at a page that is not published. Publish that page first, or choose another."),
        ("formId", formId),
        ("pageId", pageId));

    /// <summary>FR-003a: a version to roll back to shows menus that were deleted since.</summary>
    internal static Error VersionMissingMenus(IReadOnlyCollection<Guid> menuIds) => With(
        Error.Conflict(
            "pages.version_missing_menus",
            $"This version shows {menuIds.Count} menu(s) that no longer exist."),
        ("menuIds", menuIds.ToArray()));

    /// <summary>FR-003: published pages or components still show the menu.</summary>
    internal static Error MenuInUseByPublishedContent(int pageCount, int componentCount) => With(
        Error.Conflict(
            "pages.menu_in_use_by_published_content",
            $"This menu is shown by {pageCount} published page(s) and {componentCount} published component(s). Remove it from them first."),
        ("pages", pageCount),
        ("components", componentCount));

    /// <summary>A document refusal, carrying the failed rule and where it failed.</summary>
    internal static Error InvalidDocument(DocumentValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var error = result.Code switch
        {
            "pages.document_too_large" => Error.Validation(result.Code, result.Message ?? DocumentTooLarge.Message),
            null => throw new ArgumentException("A valid document is not an error.", nameof(result)),
            _ => Error.Validation(result.Code, result.Message ?? "The document does not follow its schema."),
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
