using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// One stored value through one field's plan: what happens to it and what is stored after
/// (research CS-07). Used by the preview and by the migration alike.
/// </summary>
internal sealed class ValueMigration(ItemValueValidator validator)
{
    /// <summary>
    /// The outcome for <paramref name="value"/> (null when the plan does not concern it, such
    /// as an empty value of a field that only changed type) and the value to store.
    /// </summary>
    public Converted? Apply(FieldPlan plan, JsonNode? value, Func<Guid, MediaKind> mediaKind)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(mediaKind);

        if (plan.After is null)
        {
            return value is null ? null : new Converted(ValueOutcome.Cleared, null);
        }

        if (value is null)
        {
            if (plan.RequiredTurnedOn && plan.After.Type != FieldType.Boolean)
            {
                return plan.FillIn is { } fill ? new Converted(ValueOutcome.Filled, fill.DeepClone()) : new Converted(ValueOutcome.StillMissing, null);
            }

            // A field added without "required" may still take a fill-in (FR-029).
            return plan.Before is null && plan.FillIn is { } fillIn ? new Converted(ValueOutcome.Filled, fillIn.DeepClone()) : null;
        }

        if (plan.Before is null)
        {
            return null;
        }

        var converted = FieldConversion.Convert(plan.Before, plan.After, value, mediaKind);
        if (converted.Outcome is ValueOutcome.Cleared || converted.Value is null)
        {
            return converted;
        }

        if (plan.After.Type is FieldType.Image or FieldType.File)
        {
            var kind = mediaKind(FieldConversion.MediaIdOf(converted.Value));
            if (kind == MediaKind.Missing)
            {
                return new Converted(ValueOutcome.Missing, converted.Value);
            }

            var accepted = plan.After.Type == FieldType.Image
                ? kind == MediaKind.Image
                : plan.After.Accept switch
                {
                    FileAccept.Images => kind == MediaKind.Image,
                    FileAccept.Documents => kind == MediaKind.Document,
                    _ => true,
                };

            if (!accepted)
            {
                return Invalid(converted);
            }
        }

        return validator.Check(plan.After, converted.Value) is ValueState.NeedsAttention or ValueState.Malformed
            ? Invalid(converted)
            : converted;
    }

    /// <summary>The worse of a draft's and a published copy's outcomes (research CS-07).</summary>
    public static ValueOutcome? Worse(ValueOutcome? draft, ValueOutcome? published) =>
        (draft, published) switch
        {
            (null, _) => published,
            (_, null) => draft,
            _ => draft < published ? draft : published,
        };

    /// <summary>A kept or converted value that breaks the new settings is kept and reported (FR-027).</summary>
    private static Converted Invalid(Converted converted) =>
        converted.Outcome is ValueOutcome.Kept or ValueOutcome.Converted ? converted with { Outcome = ValueOutcome.NowInvalid } : converted;
}
