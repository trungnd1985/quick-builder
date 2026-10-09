namespace QuickBuilder.Cms.Domain;

/// <summary>What kind of difference a <see cref="FieldChange"/> is (research CS-06, data-model §3.4).</summary>
internal enum ChangeKind
{
    DetailsChanged,
    FieldAdded,
    FieldRemoved,
    FieldsReordered,
    FieldRenamed,
    RequiredChanged,
    TypeChanged,
    SettingsChanged,
    OptionAdded,
    OptionRemoved,
    OptionRelabelled,
    OptionsReordered,
    TitleFieldChanged,
}

/// <summary>The names of the type settings a <see cref="ChangeKind.SettingsChanged"/> may concern.</summary>
internal static class Settings
{
    public const string Multiline = "multiline";
    public const string MaxLength = "maxLength";
    public const string Decimals = "decimals";
    public const string Min = "min";
    public const string Max = "max";
    public const string WithTime = "withTime";
    public const string Accept = "accept";
    public const string Target = "target";
    public const string Formats = "formats";
}

/// <summary>
/// One difference between a stored schema and a proposal, matched by field id (research CS-06).
/// </summary>
/// <remarks>
/// <see cref="Before"/> is the stored field and <see cref="After"/> the proposed one; either is
/// null when the field was added or removed. A setting change names its <see cref="Setting"/>,
/// and an option change its options.
/// </remarks>
internal sealed record FieldChange(ChangeKind Kind)
{
    public Field? Before { get; init; }

    public Field? After { get; init; }

    public string? Setting { get; init; }

    public FieldOption? OptionBefore { get; init; }

    public FieldOption? OptionAfter { get; init; }

    /// <summary>The field the change concerns; null for collection-wide changes.</summary>
    public string? FieldId => After?.Id ?? Before?.Id;

    /// <summary>Whether this change, on its own, can change what an item holds.</summary>
    public bool TouchesValues => Kind is ChangeKind.FieldAdded or ChangeKind.FieldRemoved or ChangeKind.RequiredChanged
        or ChangeKind.TypeChanged or ChangeKind.SettingsChanged or ChangeKind.OptionRemoved;
}
