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

    internal static Error OutOfDate { get; } = Error.Conflict(
        "editor.out_of_date",
        "The design tokens were changed by someone else since you opened them. Reload and try again.");

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

    /// <summary>The extra problem fields attached to <paramref name="error"/>, if any.</summary>
    internal static IReadOnlyDictionary<string, object?>? ExtensionsOf(Error error) =>
        ExtensionsByError.TryGetValue(error, out var extensions) ? extensions : null;

    private static Error With(Error error, params (string Name, object? Value)[] fields)
    {
        ExtensionsByError.AddOrUpdate(error, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));
        return error;
    }
}
