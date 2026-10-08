namespace QuickBuilder.Cms.Domain;

/// <summary>
/// How a change treats stored values (contract §7, research CS-07, data-model §3.2).
/// </summary>
internal enum ChangeRule
{
    /// <summary>No value can change and nothing needs reporting; saved without a preview (FR-030).</summary>
    Free,

    /// <summary>No value is lost, but the preview shows counts first (FR-023, FR-027, FR-029).</summary>
    Reported,

    /// <summary>Values are rewritten and none is lost.</summary>
    Lossless,

    /// <summary>Some values may be shortened, rounded or cleared; the member confirms the preview.</summary>
    Lossy,

    /// <summary>Refused unless no item holds a value in the field.</summary>
    Forbidden,
}

/// <summary>
/// The change rules of the collection schema (contract §7): which rule each change falls
/// under. The value conversions themselves are in the other part of this class.
/// </summary>
/// <remarks>
/// One classification drives the builder's choice to preview (<c>schemaChanges.ts</c> ports it),
/// the preview's counts, the confirmation rule and the migration, so they cannot disagree.
/// </remarks>
internal static partial class FieldConversion
{
    /// <summary>The type changes contract §7 allows, with their rule. Every other pair is forbidden.</summary>
    private static readonly Dictionary<(FieldType From, FieldType To), ChangeRule> TypeChanges = new()
    {
        [(FieldType.Text, FieldType.RichText)] = ChangeRule.Lossless,
        [(FieldType.RichText, FieldType.Text)] = ChangeRule.Lossy,
        [(FieldType.Number, FieldType.Text)] = ChangeRule.Lossless,
        [(FieldType.Text, FieldType.Number)] = ChangeRule.Lossy,
        [(FieldType.Date, FieldType.Text)] = ChangeRule.Lossless,
        [(FieldType.Text, FieldType.Date)] = ChangeRule.Lossy,
        [(FieldType.Option, FieldType.Text)] = ChangeRule.Lossless,
        [(FieldType.Text, FieldType.Option)] = ChangeRule.Lossy,
        [(FieldType.Boolean, FieldType.Text)] = ChangeRule.Lossless,
        [(FieldType.Image, FieldType.File)] = ChangeRule.Lossless,
        [(FieldType.File, FieldType.Image)] = ChangeRule.Lossy,
        [(FieldType.Reference, FieldType.MultiReference)] = ChangeRule.Lossless,
        [(FieldType.MultiReference, FieldType.Reference)] = ChangeRule.Lossy,
    };

    public static ChangeRule TypeChangeRule(FieldType from, FieldType to) =>
        TypeChanges.TryGetValue((from, to), out var rule) ? rule : ChangeRule.Forbidden;

    /// <summary>The rule of <paramref name="change"/>; <paramref name="hasFillIn"/> says whether the save gives its field a fill-in value.</summary>
    public static ChangeRule Classify(FieldChange change, bool hasFillIn = false)
    {
        ArgumentNullException.ThrowIfNull(change);

        return change.Kind switch
        {
            // Reported when items will hold something to count: a fill-in, or a required value they lack (FR-029).
            ChangeKind.FieldAdded => hasFillIn || (change.After!.Required && change.After.Type != FieldType.Boolean)
                ? ChangeRule.Reported
                : ChangeRule.Free,
            ChangeKind.FieldRemoved or ChangeKind.OptionRemoved => ChangeRule.Lossy,
            ChangeKind.RequiredChanged => change.After!.Required ? ChangeRule.Reported : ChangeRule.Free,
            ChangeKind.TypeChanged => TypeChangeRule(change.Before!.Type, change.After!.Type),
            ChangeKind.SettingsChanged => ClassifySetting(change.Setting!, change.Before!, change.After!),
            _ => ChangeRule.Free,
        };
    }

    private static ChangeRule ClassifySetting(string setting, Field before, Field after) => setting switch
    {
        Settings.Multiline => after.Multiline ? ChangeRule.Free : ChangeRule.Lossless,
        Settings.MaxLength => after.MaxLength < before.MaxLength ? ChangeRule.Reported : ChangeRule.Free,
        Settings.Decimals => after.Decimals < before.Decimals ? ChangeRule.Lossy : ChangeRule.Free,
        Settings.Min => after.Min is { } min && (before.Min is null || min > before.Min) ? ChangeRule.Reported : ChangeRule.Free,
        Settings.Max => after.Max is { } max && (before.Max is null || max < before.Max) ? ChangeRule.Reported : ChangeRule.Free,
        Settings.WithTime => after.WithTime ? ChangeRule.Lossless : ChangeRule.Lossy,
        Settings.Accept => after.Accept == FileAccept.Any ? ChangeRule.Free : ChangeRule.Reported,
        Settings.Target => ChangeRule.Forbidden,

        // 035 FR-025: values are kept; those using a group no longer allowed need attention.
        Settings.Formats => before.Formats.NarrowedBy(after.Formats) ? ChangeRule.Reported : ChangeRule.Free,
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "Not a setting of the collection schema."),
    };
}
