using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// One saved revision of a collection: who, when, and a summary of each change with its
/// counts (FR-033, research CS-13, data-model §1.4 and §3.4).
/// </summary>
/// <remarks>
/// The summary holds keys, names and labels as they were at that revision, so the history
/// reads correctly after later renames. It never holds item values or example items.
/// </remarks>
internal sealed partial class SchemaChangeEntry : ISiteOwned
{
    private SchemaChangeEntry(Guid collectionId, int revision, Guid siteId, DateTimeOffset changedAtUtc, Guid changedBy, string summary)
    {
        CollectionId = collectionId;
        Revision = revision;
        SiteId = siteId;
        ChangedAtUtc = changedAtUtc;
        ChangedBy = changedBy;
        Summary = summary;
    }

    // EF Core materialisation.
    private SchemaChangeEntry()
    {
        Summary = "[]";
    }

    public Guid CollectionId { get; private set; }

    public int Revision { get; private set; }

    public Guid SiteId { get; private set; }

    public DateTimeOffset ChangedAtUtc { get; private set; }

    public Guid ChangedBy { get; private set; }

    /// <summary>The change entries as a JSON array (data-model §3.4).</summary>
    public string Summary { get; private set; }

    public static SchemaChangeEntry Created(Collection collection, Guid actor, DateTimeOffset now) =>
        new(collection.Id, collection.Revision, collection.SiteId, now, actor, new JsonArray(new JsonObject { ["kind"] = "created" }).ToJsonString());

    public static SchemaChangeEntry For(Collection collection, CollectionDetails detailsBefore, ImpactSummary impact, Guid actor, DateTimeOffset now) =>
        new(collection.Id, collection.Revision, collection.SiteId, now, actor, Write(collection, detailsBefore, impact).ToJsonString());

    private static JsonArray Write(Collection collection, CollectionDetails detailsBefore, ImpactSummary impact)
    {
        var entries = new JsonArray();
        foreach (var item in impact.Changes)
        {
            var change = item.Change;
            var entry = new JsonObject { ["kind"] = KindName(change.Kind) };
            var field = change.After ?? change.Before;
            if (field is not null && change.Kind != ChangeKind.TitleFieldChanged)
            {
                entry["fieldId"] = field.Id;
                entry["key"] = field.Key;
                entry["name"] = field.Name;
            }

            switch (change.Kind)
            {
                case ChangeKind.DetailsChanged:
                    entry["from"] = detailsBefore.Name;
                    entry["to"] = collection.Name;
                    entry["slugFrom"] = detailsBefore.Slug;
                    entry["slugTo"] = collection.Slug;
                    break;
                case ChangeKind.FieldRenamed:
                    entry["from"] = change.Before!.Name;
                    entry["to"] = change.After!.Name;
                    entry["keyFrom"] = change.Before.Key;
                    break;
                case ChangeKind.RequiredChanged:
                    entry["to"] = change.After!.Required ? "required" : "optional";
                    break;
                case ChangeKind.TypeChanged:
                    entry["from"] = change.Before!.Type.ToName();
                    entry["to"] = change.After!.Type.ToName();
                    break;
                case ChangeKind.SettingsChanged:
                    entry["setting"] = change.Setting;
                    entry["from"] = SettingValue(change.Before!, change.Setting!);
                    entry["to"] = SettingValue(change.After!, change.Setting!);
                    break;
                case ChangeKind.OptionAdded or ChangeKind.OptionRemoved or ChangeKind.OptionRelabelled:
                    entry["optionId"] = (change.OptionAfter ?? change.OptionBefore)!.Id;
                    entry["from"] = change.OptionBefore?.Label;
                    entry["to"] = change.OptionAfter?.Label;
                    break;
                case ChangeKind.TitleFieldChanged:
                    entry["fieldId"] = change.After?.Id;
                    entry["name"] = change.After?.Name;
                    entry["from"] = change.Before?.Name;
                    break;
            }

            var counts = new JsonObject();
            foreach (var (outcome, count) in item.Counts.Where(c => c.Value > 0).OrderBy(c => c.Key))
            {
                counts[OutcomeName(outcome)] = count;
            }

            if (counts.Count > 0)
            {
                entry["counts"] = counts;
            }

            entries.Add(entry);
        }

        return entries;
    }

    public static string KindName(ChangeKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    public static string OutcomeName(ValueOutcome outcome) => JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString());

    private static JsonNode? SettingValue(Field field, string setting) => setting switch
    {
        Settings.Multiline => field.Multiline,
        Settings.MaxLength => field.MaxLength,
        Settings.Decimals => field.Decimals,
        Settings.Min => field.Min,
        Settings.Max => field.Max,
        Settings.WithTime => field.WithTime,
        Settings.Accept => field.Accept,
        Settings.Target => field.Target?.ToString(),
        Settings.Formats => field.Formats.ToString(),
        _ => null,
    };
}
