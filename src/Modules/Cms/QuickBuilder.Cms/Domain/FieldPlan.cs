using System.Text.Json.Nodes;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// What one save does to one field's stored values: the field before and after, and the
/// change that the preview reports the field's counts under (research CS-07).
/// </summary>
/// <remarks>
/// Several changes can touch one field in one save (a type change and "required", two
/// settings). Each item gets one outcome per field, measured on the field's whole change, and
/// the counts are shown under the field's most consequential change, so no item is counted
/// twice. The preview and the migration both work from these plans, so they cannot disagree.
/// </remarks>
internal sealed record FieldPlan(string FieldId, Field? Before, Field? After, FieldChange Primary, ChangeRule Rule)
{
    /// <summary>"Required" was turned on, or the field was added required, in this save.</summary>
    public bool RequiredTurnedOn { get; init; }

    /// <summary>The fill-in value for items that hold none (FR-029), with permanent ids.</summary>
    public JsonNode? FillIn { get; init; }

    /// <summary>Whether the field's values are media, before or after, so their kinds are needed.</summary>
    public bool InvolvesMedia => Before?.Type is FieldType.Image or FieldType.File || After?.Type is FieldType.Image or FieldType.File;

    /// <summary>
    /// The plans of a save: one per field whose changes are not all free. A plan's rule is the
    /// most severe of its field's changes.
    /// </summary>
    public static IReadOnlyList<FieldPlan> For(
        IReadOnlyList<FieldChange> changes,
        IReadOnlyDictionary<string, JsonNode> fillIns)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(fillIns);

        var plans = new List<FieldPlan>();
        foreach (var group in changes.Where(c => c.FieldId is not null && c.Kind != ChangeKind.TitleFieldChanged).GroupBy(c => c.FieldId!))
        {
            var hasFillIn = fillIns.ContainsKey(group.Key);
            var ranked = group
                .Select(c => (Change: c, Rule: FieldConversion.Classify(c, hasFillIn)))
                .Where(c => c.Rule != ChangeRule.Free)
                .OrderBy(c => Severity(c.Rule))
                .ToList();

            if (ranked.Count == 0)
            {
                continue;
            }

            var (primary, rule) = ranked[0];
            var first = group.First();
            plans.Add(new FieldPlan(group.Key, first.Before, first.After, primary, rule)
            {
                RequiredTurnedOn = group.Any(c => c.Kind == ChangeKind.RequiredChanged && c.After!.Required)
                    || (first.Kind == ChangeKind.FieldAdded && first.After!.Required),
                FillIn = fillIns.GetValueOrDefault(group.Key),
            });
        }

        return plans;
    }

    private static int Severity(ChangeRule rule) => rule switch
    {
        ChangeRule.Forbidden => 0,
        ChangeRule.Lossy => 1,
        ChangeRule.Lossless => 2,
        ChangeRule.Reported => 3,
        _ => 4,
    };
}
