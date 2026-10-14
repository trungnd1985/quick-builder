using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Schema;
using QuickBuilder.Pages.Domain.Menus;

namespace QuickBuilder.Pages.Infrastructure.Schema;

/// <summary>
/// Checks a menu document against the menu contract, in the order docs/contracts/menu.md §4
/// gives (024 research NV-03).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Size</b>, on the bytes as received.</item>
/// <item><b>Well-formed JSON without duplicate property names</b>, then a supported
/// <c>schemaVersion</c>.</item>
/// <item><b>The schema</b>, through <see cref="SchemaSubsetValidator"/> over the embedded file.</item>
/// <item><b><see cref="MenuRules"/></b>: ids, count, depth, labels, new tabs.</item>
/// </list>
/// Rule 8 (new targets name a page or item of the site) needs the site and the previous draft,
/// so the caller checks it last. Messages never quote document content.
/// </remarks>
internal sealed class MenuDocumentValidator
{
    private const string InvalidMenu = "pages.invalid_menu_document";

    // A three-level menu needs about ten JSON levels. Anything this deep is far past rule 5,
    // and is reported as too deep rather than as malformed.
    private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = 64 };

    private readonly Dictionary<int, SchemaSubsetValidator> _schemas = MenuSchema.SupportedVersions.ToDictionary(
        v => v,
        v =>
        {
            using var stream = MenuSchema.OpenSchema(v);
            return SchemaSubsetValidator.Load(stream);
        });

    public DocumentValidationResult Validate(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MenuSchema.MaxDocumentBytes)
        {
            return DocumentValidationResult.Invalid(
                "pages.document_too_large", "document_too_large", string.Empty,
                $"A menu may be at most {MenuSchema.MaxDocumentBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray(), ParseOptions);
        }
        catch (JsonException ex) when (ex.Message.Contains("maximum configured depth", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("too_deep", string.Empty, $"Entries may be nested at most {MenuSchema.MaxLevels} levels deep.");
        }
        catch (JsonException)
        {
            return Invalid("malformed_json", string.Empty, "The menu is not well-formed JSON.");
        }

        using (document)
        {
            return Validate(document.RootElement);
        }
    }

    private DocumentValidationResult Validate(JsonElement root)
    {
        if (PageDocumentValidator.FindDuplicateProperty(root, string.Empty) is { } duplicate)
        {
            return Invalid("duplicate_property", duplicate, "A property name appears twice in one object.");
        }

        var version = MenuSchema.CurrentVersion;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("schemaVersion", out var declared)
            && declared.ValueKind == JsonValueKind.Number
            && declared.TryGetInt32(out var declaredVersion))
        {
            if (!MenuSchema.SupportedVersions.Contains(declaredVersion))
            {
                return DocumentValidationResult.Invalid(
                    "pages.unsupported_schema_version", "unsupported_schema_version", "/schemaVersion",
                    "This installation does not support that menu version.");
            }

            version = declaredVersion;
        }

        if (_schemas[version].Validate(root) is { } failure)
        {
            var (rule, pointer) = MenuRules.FromSchema(failure);
            return Invalid(rule, pointer, $"The menu is not valid: {failure.Message}");
        }

        if (MenuRules.Check(root) is { } broken)
        {
            return Invalid(broken.Rule, broken.Pointer, broken.Message);
        }

        return DocumentValidationResult.Valid(version, new HashSet<Guid>());
    }

    private static DocumentValidationResult Invalid(string rule, string pointer, string message) =>
        DocumentValidationResult.Invalid(InvalidMenu, rule, pointer, message);
}
