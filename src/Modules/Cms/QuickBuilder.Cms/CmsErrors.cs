using System.Runtime.CompilerServices;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms;

/// <summary>
/// Every expected failure the Cms module reports, by code (014 data-model §5).
/// </summary>
/// <remarks>
/// Some refusals carry more than a code and a message: which rule a schema broke and where,
/// the current revision, a fresh preview, the collections that refer to one being deleted.
/// As in <c>PagesErrors</c>, those fields ride alongside the error instance and
/// <see cref="CmsErrorResults"/> copies them into the problem's extensions. Messages never
/// quote names, keys or values (Principle IX).
/// </remarks>
internal static class CmsErrors
{
    private static readonly ConditionalWeakTable<Error, IReadOnlyDictionary<string, object?>> ExtensionsByError = [];

    internal static Error SiteNotFound { get; } = Error.NotFound("sites.not_found", "No such site.");

    internal static Error CollectionNotFound { get; } =
        Error.NotFound("cms.collection_not_found", "No such collection on this site.");

    internal static Error SlugTaken { get; } = Error.Conflict(
        "cms.slug_taken", "Another collection of this site already uses this address name.");

    internal static Error CollectionBusy { get; } = Error.Conflict(
        "cms.collection_busy", "This collection is being changed by someone else right now. Try again shortly.");

    internal static Error ItemsBeingUpdated { get; } = Error.Conflict(
        "cms.items_being_updated", "The fields of this collection are being updated. Try again shortly.");

    internal static Error TooManyCollections() => With(
        Error.Conflict("cms.too_many_collections", $"A site can hold at most {CollectionSchemaLimits.MaxCollectionsPerSite} collections."),
        ("limit", CollectionSchemaLimits.MaxCollectionsPerSite));

    internal static Error ProposalTooLarge() => Error.Validation(
        "cms.proposal_too_large", $"A collection definition may be at most {CollectionSchemaLimits.MaxProposalBytes / 1024} KiB.");

    /// <summary>A schema or detail that breaks the contract, with its rule and where (data-model §5).</summary>
    internal static Error Invalid(string code, string rule, string pointer, string message) =>
        With(Error.Validation(code, message), ("rule", rule), ("pointer", pointer));

    internal static Error RevisionConflict(int currentRevision) => With(
        Error.Conflict("cms.revision_conflict", "This collection was changed by someone else since you opened it. Reload and try again."),
        ("currentRevision", currentRevision));

    internal static Error UnknownTargetCollection(string pointer) => With(
        Error.Validation("cms.unknown_target_collection", "A reference field must target a collection of this site."),
        ("pointer", pointer));

    internal static Error TypeChangeNotAllowed(string fieldId, string from, string to) => With(
        Error.Validation("cms.type_change_not_allowed", "This change of type is not possible while items hold values in the field. Add a new field instead."),
        ("fieldId", fieldId), ("from", from), ("to", to));

    internal static Error TargetChangeNotAllowed(string fieldId) => With(
        Error.Validation("cms.target_change_not_allowed", "A reference field cannot target another collection while items hold values in it. Add a new field instead."),
        ("fieldId", fieldId));

    internal static Error InvalidFillIn(string fieldId, string pointer) => With(
        Error.Validation("cms.invalid_fill_in", "The fill-in value is not a valid value for its field, or the field takes none."),
        ("fieldId", fieldId), ("pointer", pointer));

    internal static Error ConfirmationRequired(object preview) => With(
        Error.Conflict("cms.confirmation_required", "This change removes or shortens stored values. Review the preview and confirm it."),
        ("preview", preview));

    internal static Error PreviewOutdated(object preview) => With(
        Error.Conflict("cms.preview_outdated", "The items changed since the preview was shown. Review the new preview and confirm again."),
        ("preview", preview));

    internal static Error CollectionReferenced(IReadOnlyList<object> references) => With(
        Error.Conflict("cms.collection_referenced", "Other collections have reference fields that target this collection. Remove or change them first."),
        ("references", references));

    // Items (015 data-model §6).

    internal static Error ItemNotFound { get; } = Error.NotFound("cms.item_not_found", "No such item in this collection.");

    internal static Error VersionNotFound { get; } = Error.NotFound("cms.version_not_found", "That version is no longer kept.");

    internal static Error ScheduleNotFound { get; } = Error.NotFound("cms.schedule_not_found", "Nothing of that kind is scheduled for this item.");

    internal static Error ItemNotPublished { get; } = Error.Conflict(
        "cms.item_not_published", "This item has nothing published, and no publish is scheduled.");

    internal static Error ScheduleInPast { get; } = Error.Validation("cms.schedule_in_past", "Choose a time in the future.");

    internal static Error ScheduleOrder { get; } = Error.Validation(
        "cms.schedule_order", "A scheduled unpublish must come after the scheduled publish.");

    internal static Error ItemTooLarge() => Error.Validation(
        "cms.item_too_large", $"An item may be at most {MaxItemRequestBytes / 1024 / 1024} MiB.");

    internal const int MaxItemRequestBytes = 1_048_576;

    internal static Error InvalidItem(object report) => With(
        Error.Validation("cms.invalid_item", "Some values are not valid for their fields. Fix them and save again."),
        ("report", report));

    internal static Error ItemNeedsAttention(object report) => With(
        Error.Conflict("cms.item_needs_attention", "Some fields need attention before this item can be published."),
        ("report", report));

    internal static Error VersionNotPublishable(object report) => With(
        Error.Conflict("cms.version_not_publishable", "That version no longer meets today's rules, so it cannot be published again. You can copy it into the draft."),
        ("report", report));

    internal static Error ItemSlugTaken(string suggestion) => With(
        Error.Conflict("cms.item_slug_taken", "Another item of this collection already uses this slug."),
        ("suggestion", suggestion));

    /// <summary>035 FR-020: an admin from before 035 would drop rich text formats it does not know.</summary>
    internal static Error AdminOutdated { get; } = Error.Conflict(
        "cms.admin_outdated",
        "This page of the admin is out of date. Reload it to keep editing; your last change was not saved.");

    internal static Error ItemConflict(int currentRevision) => With(
        Error.Conflict("cms.item_conflict", "This item was changed by someone else since you opened it. Reload and try again."),
        ("currentRevision", currentRevision));

    internal static Error SchemaChanged(int schemaRevision) => With(
        Error.Conflict("cms.schema_changed", "The fields of this collection changed since you opened this item. Reload the form; your entries are kept."),
        ("schemaRevision", schemaRevision));

    internal static Error ItemReferenced(IReadOnlyList<object> referrers, int total) => With(
        Error.Conflict("cms.item_referenced", "Other items refer to this item. Change their references first."),
        ("referrers", referrers), ("total", total));

    internal static Error SchedulePending(object schedule) => With(
        Error.Conflict("cms.schedule_pending", "A publish is scheduled for this item. Confirm that it should be cancelled."),
        ("schedule", schedule));

    internal static Error InvalidQuery(string parameter) => With(
        Error.Validation("cms.invalid_query", "The list query is not valid for this collection."),
        ("parameter", parameter));

    internal static IReadOnlyDictionary<string, object?>? ExtensionsOf(Error error) =>
        ExtensionsByError.TryGetValue(error, out var extensions) ? extensions : null;

    private static Error With(Error error, params (string Name, object? Value)[] fields)
    {
        ExtensionsByError.AddOrUpdate(error, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));
        return error;
    }
}
