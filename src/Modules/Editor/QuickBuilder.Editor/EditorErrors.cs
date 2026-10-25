using System.Runtime.CompilerServices;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor;

/// <summary>
/// Every expected failure the Editor module reports, by code (011 data-model §6).
/// </summary>
/// <remarks>
/// A token set refusal carries a JSON Pointer to where it failed. <see cref="Error"/> has no
/// room for that, so, as in <c>PagesErrors</c>, the pointer rides alongside the error instance
/// and <see cref="EditorErrorResults"/> copies it into the problem's extensions.
/// </remarks>
internal static class EditorErrors
{
    private static readonly ConditionalWeakTable<Error, IReadOnlyDictionary<string, object?>> ExtensionsByError = [];

    internal static Error SiteNotFound { get; } = Error.NotFound("sites.not_found", "No such site.");

    /// <summary>019 FR-026: stored by a newer release; this one neither shows nor overwrites it.</summary>
    internal static Error DocumentTooNew { get; } = Error.Conflict(
        "editor.document_too_new",
        "These design tokens were saved by a newer version of QuickBuilder and cannot be opened here. Nothing was changed.");

    /// <summary>019 FR-025: an older token set that could not be brought up to date.</summary>
    internal static Error DocumentUpgradeFailed { get; } = Error.Validation(
        "editor.document_upgrade_failed",
        "These design tokens were saved by an older version of QuickBuilder and could not be brought up to date. Ask your administrator to check the logs.");

    internal static Error OutOfDate { get; } = Error.Conflict(
        "editor.out_of_date",
        "The design tokens were changed by someone else since you opened them. Reload and try again.");

    /// <summary>012 data-model §6: a rollback or read of a design version that is not kept.</summary>
    internal static Error DesignVersionNotFound { get; } = Error.NotFound(
        "editor.design_version_not_found",
        "No such design version is kept for this site.");

    internal static Error TooLarge { get; } = Error.Validation(
        "editor.tokens_too_large",
        $"A token set may be at most {DesignTokens.MaxDocumentBytes} bytes.");

    internal static Error UnsupportedSchemaVersion { get; } = With(
        Error.Validation("editor.unsupported_schema_version", "This installation does not support that token set version."),
        ("pointer", "/schemaVersion"));

    internal static Error InvalidTokens(string pointer, string message) => With(
        Error.Validation("editor.invalid_tokens", $"The token set is not valid: {message}"),
        ("pointer", pointer));

    internal static Error TokenRequired(string pointer) => With(
        Error.Validation("editor.token_required", "A token set needs at least one colour, one text size and one spacing step."),
        ("pointer", pointer));

    internal static Error DuplicateTokenId(string pointer) => With(
        Error.Validation("editor.duplicate_token_id", "Two tokens have the same id. Ids are unique across colours, text sizes and spacing."),
        ("pointer", pointer));

    internal static Error DuplicateTokenName(string pointer) => With(
        Error.Validation("editor.duplicate_token_name", "Two tokens of the same kind have the same name."),
        ("pointer", pointer));

    internal static Error ContentWidthsOrder { get; } = With(
        Error.Validation("editor.content_widths_order", "The content widths must go from narrowest to widest: Narrow, then Normal, then Wide."),
        ("pointer", "/contentWidths"));

    /// <summary>023 data-model §5: someone else's session holds the document's lock.</summary>
    internal static Error LockHeld(Guid holderId, string holderName, DateTimeOffset since, bool stale) => With(
        Error.Conflict("editing.lock_held", "Someone else is editing this. Nothing was changed."),
        ("holder", new { id = holderId, displayName = holderName }),
        ("since", since),
        ("stale", stale));

    /// <summary>023 data-model §5: a takeover of a lock that is active, or held by someone else now.</summary>
    internal static Error NotStale(Guid holderId, string holderName, DateTimeOffset since, int inactiveSeconds) => With(
        Error.Conflict("editing.not_stale", "This lock can't be taken over: its holder is active again, or someone else holds it now."),
        ("holder", new { id = holderId, displayName = holderName }),
        ("since", since),
        ("inactiveSeconds", inactiveSeconds));

    /// <summary>The session expired, was released, or its document was deleted. The admin opens a new one.</summary>
    internal static Error SessionGone { get; } = Error.NotFound(
        "editing.session_gone",
        "This editing session has ended. Reopen the document.");

    internal static Error EditingForbidden { get; } = Error.Conflict(
        "editing.forbidden",
        "You can't edit this.");

    /// <summary>The extra problem fields attached to <paramref name="error"/>, if any.</summary>
    internal static IReadOnlyDictionary<string, object?>? ExtensionsOf(Error error) =>
        ExtensionsByError.TryGetValue(error, out var extensions) ? extensions : null;

    private static Error With(Error error, params (string Name, object? Value)[] fields)
    {
        ExtensionsByError.AddOrUpdate(error, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));
        return error;
    }
}
