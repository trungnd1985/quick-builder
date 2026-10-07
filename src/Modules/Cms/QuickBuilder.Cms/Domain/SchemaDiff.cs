namespace QuickBuilder.Cms.Domain;

/// <summary>A collection's own details: what a save may change beside the schema.</summary>
internal sealed record CollectionDetails(string Name, string Slug, string? Description);

/// <summary>
/// The differences between a stored collection and a proposal, matched by field id
/// (research CS-06). A rename is never mistaken for a removal plus an addition.
/// </summary>
/// <remarks>
/// The proposal's ids must already be permanent (<see cref="IdAssignment"/>), and
/// <paramref name="addedIds"/> says which of them were assigned now. The order of the result
/// is fixed — details, then the proposal's fields in order, then removals in stored order, then
/// the field order and the title field — so the same save always has the same change list and
/// the same impact digest.
/// </remarks>
internal static class SchemaDiff
{
    public static IReadOnlyList<FieldChange> Compare(
        CollectionDetails detailsBefore,
        CollectionSchemaDocument before,
        CollectionDetails detailsAfter,
        CollectionSchemaDocument after,
        IReadOnlySet<string> addedIds)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(addedIds);

        var changes = new List<FieldChange>();

        if (detailsBefore != detailsAfter)
        {
            changes.Add(new FieldChange(ChangeKind.DetailsChanged));
        }

        foreach (var field in after.Fields)
        {
            if (addedIds.Contains(field.Id) || before.Find(field.Id) is not { } old)
            {
                changes.Add(new FieldChange(ChangeKind.FieldAdded) { After = field });
                continue;
            }

            CompareField(old, field, changes);
        }

        foreach (var old in before.Fields)
        {
            if (after.Find(old.Id) is null)
            {
                changes.Add(new FieldChange(ChangeKind.FieldRemoved) { Before = old });
            }
        }

        var kept = before.Fields.Select(f => f.Id).Where(id => after.Find(id) is not null).ToList();
        var keptInNewOrder = after.Fields.Select(f => f.Id).Where(id => before.Find(id) is not null).ToList();
        if (!kept.SequenceEqual(keptInNewOrder, StringComparer.Ordinal))
        {
            changes.Add(new FieldChange(ChangeKind.FieldsReordered));
        }

        if (before.TitleFieldId != after.TitleFieldId)
        {
            changes.Add(new FieldChange(ChangeKind.TitleFieldChanged)
            {
                Before = before.TitleField,
                After = after.TitleField,
            });
        }

        return changes;
    }

    private static void CompareField(Field old, Field field, List<FieldChange> changes)
    {
        if (old.Name != field.Name || old.Key != field.Key || old.Help != field.Help)
        {
            changes.Add(new FieldChange(ChangeKind.FieldRenamed) { Before = old, After = field });
        }

        if (old.Required != field.Required)
        {
            changes.Add(new FieldChange(ChangeKind.RequiredChanged) { Before = old, After = field });
        }

        if (old.Type != field.Type)
        {
            // The new type's settings come with it; a setting-by-setting comparison across types means nothing.
            changes.Add(new FieldChange(ChangeKind.TypeChanged) { Before = old, After = field });
            return;
        }

        void Setting(string name, bool differs)
        {
            if (differs)
            {
                changes.Add(new FieldChange(ChangeKind.SettingsChanged) { Before = old, After = field, Setting = name });
            }
        }

        switch (field.Type)
        {
            case FieldType.Text:
                Setting(Settings.Multiline, old.Multiline != field.Multiline);
                Setting(Settings.MaxLength, old.MaxLength != field.MaxLength);
                break;
            case FieldType.Number:
                Setting(Settings.Decimals, old.Decimals != field.Decimals);
                Setting(Settings.Min, old.Min != field.Min);
                Setting(Settings.Max, old.Max != field.Max);
                break;
            case FieldType.Date:
                Setting(Settings.WithTime, old.WithTime != field.WithTime);
                break;
            case FieldType.File:
                Setting(Settings.Accept, old.Accept != field.Accept);
                break;
            case FieldType.Reference or FieldType.MultiReference:
                Setting(Settings.Target, old.Target != field.Target);
                break;
            case FieldType.Option:
                CompareOptions(old, field, changes);
                break;
        }
    }

    private static void CompareOptions(Field old, Field field, List<FieldChange> changes)
    {
        foreach (var option in field.Options)
        {
            if (old.FindOption(option.Id) is not { } previous)
            {
                changes.Add(new FieldChange(ChangeKind.OptionAdded) { Before = old, After = field, OptionAfter = option });
            }
            else if (previous.Label != option.Label)
            {
                changes.Add(new FieldChange(ChangeKind.OptionRelabelled) { Before = old, After = field, OptionBefore = previous, OptionAfter = option });
            }
        }

        foreach (var previous in old.Options)
        {
            if (field.FindOption(previous.Id) is null)
            {
                changes.Add(new FieldChange(ChangeKind.OptionRemoved) { Before = old, After = field, OptionBefore = previous });
            }
        }

        var kept = old.Options.Select(o => o.Id).Where(id => field.FindOption(id) is not null);
        var keptInNewOrder = field.Options.Select(o => o.Id).Where(id => old.FindOption(id) is not null);
        if (!kept.SequenceEqual(keptInNewOrder, StringComparer.Ordinal))
        {
            changes.Add(new FieldChange(ChangeKind.OptionsReordered) { Before = old, After = field });
        }
    }
}
