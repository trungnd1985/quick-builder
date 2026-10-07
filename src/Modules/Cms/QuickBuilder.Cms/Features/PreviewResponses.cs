using System.Globalization;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Features;

/// <summary>An item a preview names: its id, title and what happens to it.</summary>
public sealed record PreviewExampleResponse(Guid ItemId, string? Title, string Outcome);

/// <summary>One change of a save and its effect (contracts/cms-api.yaml, <c>PreviewChange</c>).</summary>
public sealed record PreviewChangeResponse(
    string Kind,
    string Rule,
    string? FieldId,
    string? Key,
    string? Name,
    string? Setting,
    string? OptionId,
    string? From,
    string? To,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<PreviewExampleResponse> Examples,
    bool FillInAllowed);

/// <summary>What saving a proposal would do (contracts/cms-api.yaml, <c>Preview</c>; research CS-08).</summary>
public sealed record PreviewResponse(
    int Revision,
    int ItemCount,
    bool RequiresConfirmation,
    string Digest,
    IReadOnlyList<PreviewChangeResponse> Changes)
{
    internal static PreviewResponse From(int revision, ImpactSummary impact) => new(
        revision,
        impact.ItemCount,
        impact.RequiresConfirmation,
        ImpactDigest.Compute(impact),
        [.. impact.Changes.Select(ToChange)]);

    private static PreviewChangeResponse ToChange(ChangeImpact impact)
    {
        var change = impact.Change;
        var field = change.After ?? change.Before;
        var (from, to) = change.Kind switch
        {
            ChangeKind.FieldRenamed => (change.Before!.Name, change.After!.Name),
            ChangeKind.TypeChanged => (change.Before!.Type.ToName(), change.After!.Type.ToName()),
            ChangeKind.SettingsChanged => (Setting(change.Before!, change.Setting!), Setting(change.After!, change.Setting!)),
            ChangeKind.RequiredChanged => (null, change.After!.Required ? "required" : "optional"),
            ChangeKind.OptionAdded or ChangeKind.OptionRemoved or ChangeKind.OptionRelabelled => (change.OptionBefore?.Label, change.OptionAfter?.Label),
            ChangeKind.TitleFieldChanged => (change.Before?.Name, change.After?.Name),
            _ => (null, null),
        };

        return new PreviewChangeResponse(
            SchemaChangeEntry.KindName(change.Kind),
            System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(impact.Rule.ToString()),
            field?.Id,
            field?.Key,
            field?.Name,
            change.Setting,
            (change.OptionAfter ?? change.OptionBefore)?.Id,
            from,
            to,
            impact.Counts.Where(c => c.Value > 0).OrderBy(c => c.Key).ToDictionary(c => SchemaChangeEntry.OutcomeName(c.Key), c => c.Value),
            [.. impact.Examples.Select(e => new PreviewExampleResponse(e.ItemId, e.Title, SchemaChangeEntry.OutcomeName(e.Outcome)))],
            impact.FillInAllowed);
    }

    private static string? Setting(Field field, string setting) => setting switch
    {
        Settings.Multiline => field.Multiline ? "true" : "false",
        Settings.MaxLength => field.MaxLength.ToString(CultureInfo.InvariantCulture),
        Settings.Decimals => field.Decimals.ToString(CultureInfo.InvariantCulture),
        Settings.Min => field.Min is { } min ? FieldConversion.NumberToText(min) : null,
        Settings.Max => field.Max is { } max ? FieldConversion.NumberToText(max) : null,
        Settings.WithTime => field.WithTime ? "true" : "false",
        Settings.Accept => field.Accept,
        Settings.Target => field.Target?.ToString(),
        _ => null,
    };
}
