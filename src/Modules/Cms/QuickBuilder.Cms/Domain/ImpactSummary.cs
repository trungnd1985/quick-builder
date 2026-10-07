namespace QuickBuilder.Cms.Domain;

/// <summary>
/// What one change does to one item, measured on the worse of its draft and published copies
/// (research CS-07). Declared in that order: a lower value is worse.
/// </summary>
internal enum ValueOutcome
{
    Cleared,
    PartlyLost,
    NowInvalid,
    StillMissing,
    Missing,
    Filled,
    Converted,
    Kept,
}

/// <summary>An item a preview names as an example: its id and its title-field text.</summary>
internal sealed record ImpactExample(Guid ItemId, string? Title, ValueOutcome Outcome);

/// <summary>One change of a save and what it does to the stored items (research CS-08).</summary>
internal sealed record ChangeImpact(FieldChange Change, ChangeRule Rule)
{
    /// <summary>Items per outcome. Zero counts are absent.</summary>
    public IReadOnlyDictionary<ValueOutcome, int> Counts { get; init; } = new Dictionary<ValueOutcome, int>();

    /// <summary>Up to ten items, for the preview only; never stored or digested.</summary>
    public IReadOnlyList<ImpactExample> Examples { get; init; } = [];

    /// <summary>Whether the change's field may take a fill-in value in this save (FR-029, FR-031).</summary>
    public bool FillInAllowed { get; init; }

    public int CountOf(ValueOutcome outcome) => Counts.TryGetValue(outcome, out var count) ? count : 0;

    /// <summary>Values would be cleared or partly lost, so the member must confirm (FR-023).</summary>
    public bool LosesValues => CountOf(ValueOutcome.Cleared) > 0 || CountOf(ValueOutcome.PartlyLost) > 0;
}

/// <summary>
/// What a save does to a collection's items: every change with its rule and counts
/// (research CS-07, CS-08).
/// </summary>
internal sealed record ImpactSummary(IReadOnlyList<ChangeImpact> Changes, int ItemCount)
{
    public static ImpactSummary None { get; } = new([], 0);

    /// <summary>Some change would clear or partly lose values, so the save needs the member's confirmation.</summary>
    public bool RequiresConfirmation => Changes.Any(c => c.LosesValues);

    /// <summary>Some stored value will be rewritten when the save is applied.</summary>
    public bool RewritesItems => Changes.Any(c =>
        c.CountOf(ValueOutcome.Cleared) + c.CountOf(ValueOutcome.PartlyLost) + c.CountOf(ValueOutcome.Converted) + c.CountOf(ValueOutcome.Filled) > 0);

    /// <summary>The builder must preview before saving: some change is not free (FR-030).</summary>
    public bool NeedsPreview => Changes.Any(c => c.Rule != ChangeRule.Free);
}
