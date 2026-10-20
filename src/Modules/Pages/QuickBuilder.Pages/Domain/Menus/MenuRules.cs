using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.Pages.Domain.Menus;

/// <summary>A broken menu rule: its name, where, and a message that quotes no content.</summary>
internal sealed record MenuRuleFailure(string Rule, string Pointer, string Message);

/// <summary>
/// The rules of a menu document the schema cannot state (docs/contracts/menu.md §4 rules 3–7,
/// 024 research NV-03).
/// </summary>
/// <remarks>
/// Pure, over a document that has already passed the schema. Each rule is its own pass in
/// document order (depth first: an entry, its children, then its next sibling), and the rules
/// run in the contract's order, so a given document always fails the same way. The admin's
/// <c>menuValidator.ts</c> mirrors this order; <c>MenuSchemaGoldenTests</c> and
/// <c>menuSchema.spec.ts</c> hold both to the same files.
/// </remarks>
internal static class MenuRules
{
    /// <summary>The first broken rule, or null when the document keeps every rule.</summary>
    public static MenuRuleFailure? Check(JsonElement document)
    {
        var entries = Walk(document.GetProperty("entries")).ToList();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!ids.Add(entry.Element.GetProperty("id").GetString()!))
            {
                return new("duplicate_entry_id", $"{entry.Pointer}/id", "Another entry of this menu has the same id.");
            }
        }

        if (entries.Count > MenuSchema.MaxEntries)
        {
            return new("too_many_entries", entries[MenuSchema.MaxEntries].Pointer, $"A menu may hold at most {MenuSchema.MaxEntries} entries.");
        }

        if (entries.FirstOrDefault(e => e.Level > MenuSchema.MaxLevels) is { Element.ValueKind: JsonValueKind.Object } tooDeep)
        {
            return new("too_deep", tooDeep.Pointer, $"Entries may be nested at most {MenuSchema.MaxLevels} levels deep.");
        }

        foreach (var entry in entries)
        {
            var type = TargetType(entry.Element);
            if (type is "url" or "anchor" or "none" && string.IsNullOrWhiteSpace(Label(entry.Element)))
            {
                return new("label_required", entry.Pointer, "This kind of entry needs a label.");
            }
        }

        foreach (var entry in entries)
        {
            if (TargetType(entry.Element) is "anchor" or "none"
                && entry.Element.TryGetProperty("newTab", out var newTab) && newTab.ValueKind == JsonValueKind.True)
            {
                return new("new_tab_not_allowed", $"{entry.Pointer}/newTab", "Only entries that link to a page, an item or an address can open in a new tab.");
            }
        }

        return null;
    }

    /// <summary>Every entry with its JSON Pointer and level (1 at the top), in document order.</summary>
    public static IEnumerable<(JsonElement Element, string Pointer, int Level)> Walk(JsonElement entries, string pointer = "/entries", int level = 1)
    {
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var here = $"{pointer}/{index++}";
            yield return (entry, here, level);

            foreach (var child in Walk(entry.GetProperty("children"), $"{here}/children", level + 1))
            {
                yield return child;
            }
        }
    }

    /// <summary>The entry's target type: <c>page</c>, <c>item</c>, <c>url</c>, <c>anchor</c> or <c>none</c>.</summary>
    public static string? TargetType(JsonElement entry) =>
        entry.TryGetProperty("target", out var target) && target.TryGetProperty("type", out var type) ? type.GetString() : null;

    private static string? Label(JsonElement entry) =>
        entry.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : null;

    /// <summary>The pointer of a schema failure as the contract reports it (rule 2).</summary>
    public static (string Rule, string Pointer) FromSchema(SchemaFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        // A failed oneOf of a target is reported at its type, as page-schema props are.
        var pointer = failure.Keyword == "discriminator" ? $"{failure.Location}/type" : failure.Location;
        return (failure.Location.StartsWith("/entries/", StringComparison.Ordinal) ? "entry_invalid" : "schema_violation", pointer);
    }
}
