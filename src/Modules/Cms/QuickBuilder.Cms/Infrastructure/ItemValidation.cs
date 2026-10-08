using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>What kind each media file named by some values is, for one site (014 CS-12).</summary>
internal interface IItemMediaLookup
{
    Task<Func<Guid, MediaKind>> LookupAsync(Guid siteId, IEnumerable<JsonNode?> values, CancellationToken cancellationToken);
}

/// <summary>Which collection each of some items belongs to, for one site; absent when the item does not exist.</summary>
internal interface IItemReferenceLookup
{
    Task<IReadOnlyDictionary<Guid, Guid>> CollectionsOfAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the report of one copy of an item against its schema (015 research IT-05): per field,
/// the shape and settings through 014's <see cref="ItemValueValidator"/>, then the two checks
/// that need the database, media and referenced items, one query each for the whole item.
/// </summary>
/// <remarks>
/// One report serves every caller: a draft save refuses it when it has malformed problems, a
/// publish when it has any, and the indexer stores it as the item's attention. Each field has
/// at most one problem, its first; problems follow the slug, then schema order, then keys that
/// are not fields.
/// </remarks>
internal sealed class ItemValidation(ItemValueValidator validator, IItemMediaLookup media, IItemReferenceLookup references)
{
    public Task<ItemReport> ValidateAsync(
        Guid siteId,
        Guid? itemId,
        CollectionSchemaDocument schema,
        string slug,
        ItemValues values,
        CancellationToken cancellationToken) =>
        ValidateAsync(siteId, itemId, schema, slug, values, media, references, cancellationToken);

    /// <summary>The same, with lookups a caller has already filled for a whole batch (<c>ItemIndexer</c>).</summary>
    public async Task<ItemReport> ValidateAsync(
        Guid siteId,
        Guid? itemId,
        CollectionSchemaDocument schema,
        string slug,
        ItemValues values,
        IItemMediaLookup mediaLookup,
        IItemReferenceLookup referenceLookup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(values);

        var problems = new List<ItemProblem>();
        if (!SlugRules.IsValid(slug))
        {
            problems.Add(new ItemProblem(ItemReport.SlugField, ItemProblems.InvalidSlug, SlugRules.MaxLength));
        }

        var byField = new Dictionary<string, ItemProblem>(StringComparer.Ordinal);
        var mediaFields = new List<(Field Field, JsonNode Value)>();
        var referenceFields = new List<(Field Field, IReadOnlyList<Guid> Ids)>();

        foreach (var field in schema.Fields)
        {
            var value = values.Get(field.Id);
            if (Check(field, value) is { } problem)
            {
                byField[field.Id] = problem;
            }
            else if (value is not null && field.Type is FieldType.Image or FieldType.File)
            {
                mediaFields.Add((field, value));
            }
            else if (value is not null && field.Type is FieldType.Reference or FieldType.MultiReference)
            {
                referenceFields.Add((field, ReferenceIds(value)));
            }
        }

        await CheckMediaAsync(siteId, mediaLookup, mediaFields, byField, cancellationToken).ConfigureAwait(false);
        await CheckReferencesAsync(siteId, itemId, referenceLookup, referenceFields, byField, cancellationToken).ConfigureAwait(false);

        problems.AddRange(schema.Fields.Where(f => byField.ContainsKey(f.Id)).Select(f => byField[f.Id]));
        problems.AddRange(values.FieldIds
            .Where(id => schema.Find(id) is null)
            .Select(id => new ItemProblem(id, ItemProblems.UnknownField)));

        return problems.Count == 0 ? ItemReport.Clean : new ItemReport(problems);
    }

    /// <summary>The ids a reference or multi-reference value names, in order.</summary>
    public static IReadOnlyList<Guid> ReferenceIds(JsonNode value) => value switch
    {
        JsonArray array => array.Select(n => Guid.Parse(n!.GetValue<string>(), CultureInfo.InvariantCulture)).ToList(),
        _ => [Guid.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture)],
    };

    /// <summary>The problem of one value that needs no database, or null.</summary>
    private ItemProblem? Check(Field field, JsonNode? value)
    {
        if (field.Type == FieldType.MultiReference && value is JsonArray { Count: > CollectionSchemaLimits.MaxReferences })
        {
            return new ItemProblem(field.Id, ItemProblems.TooManyReferences, CollectionSchemaLimits.MaxReferences);
        }

        return validator.Check(field, value) switch
        {
            ValueState.MissingRequired => new ItemProblem(field.Id, ItemProblems.MissingRequired),
            ValueState.Malformed => new ItemProblem(field.Id, ItemProblems.Malformed),
            ValueState.NeedsAttention => SettingProblem(field, value!),
            _ => null,
        };
    }

    /// <summary>Which setting a well-formed value breaks.</summary>
    private static ItemProblem SettingProblem(Field field, JsonNode value)
    {
        switch (field.Type)
        {
            case FieldType.Text:
                var text = value.GetValue<string>();
                return text.EnumerateRunes().Count() > field.MaxLength
                    ? new ItemProblem(field.Id, ItemProblems.TooLong, field.MaxLength)
                    : new ItemProblem(field.Id, ItemProblems.NotSingleLine);
            case FieldType.Number:
                var number = value.GetValue<decimal>();
                if (decimal.Round(number, field.Decimals) != number)
                {
                    return new ItemProblem(field.Id, ItemProblems.TooManyDecimals, field.Decimals);
                }

                return field.Min is { } min && number < min
                    ? new ItemProblem(field.Id, ItemProblems.OutOfRange, min)
                    : new ItemProblem(field.Id, ItemProblems.OutOfRange, field.Max);
            case FieldType.Option:
                // A well-formed option id the field does not have: never stored (FR-013).
                return new ItemProblem(field.Id, ItemProblems.UnknownOption);
            case FieldType.MultiReference:
                return new ItemProblem(field.Id, ItemProblems.DuplicateReference);
            default:
                return new ItemProblem(field.Id, ItemProblems.Malformed);
        }
    }

    private static async Task CheckMediaAsync(
        Guid siteId,
        IItemMediaLookup media,
        List<(Field Field, JsonNode Value)> fields,
        Dictionary<string, ItemProblem> byField,
        CancellationToken cancellationToken)
    {
        if (fields.Count == 0)
        {
            return;
        }

        var kindOf = await media.LookupAsync(siteId, fields.Select(f => f.Value), cancellationToken).ConfigureAwait(false);
        foreach (var (field, value) in fields)
        {
            var kind = kindOf(FieldConversion.MediaIdOf(value));
            var allowed = field.Type == FieldType.Image ? FileAccept.Images : field.Accept;
            if (kind == MediaKind.Missing)
            {
                byField[field.Id] = new ItemProblem(field.Id, ItemProblems.MediaMissing);
            }
            else if ((allowed == FileAccept.Images && kind != MediaKind.Image) || (allowed == FileAccept.Documents && kind != MediaKind.Document))
            {
                byField[field.Id] = new ItemProblem(field.Id, ItemProblems.FileKind);
            }
        }
    }

    private static async Task CheckReferencesAsync(
        Guid siteId,
        Guid? itemId,
        IItemReferenceLookup references,
        List<(Field Field, IReadOnlyList<Guid> Ids)> fields,
        Dictionary<string, ItemProblem> byField,
        CancellationToken cancellationToken)
    {
        if (fields.Count == 0)
        {
            return;
        }

        var ids = fields.SelectMany(f => f.Ids).ToHashSet();
        var collectionOf = await references.CollectionsOfAsync(siteId, ids, cancellationToken).ConfigureAwait(false);

        foreach (var (field, fieldIds) in fields)
        {
            foreach (var id in fieldIds)
            {
                ItemProblem? problem = id == itemId
                    ? new ItemProblem(field.Id, ItemProblems.ReferenceSelf)
                    : !collectionOf.TryGetValue(id, out var collectionId)
                        ? new ItemProblem(field.Id, ItemProblems.ReferenceMissing)
                        : collectionId != field.Target
                            ? new ItemProblem(field.Id, ItemProblems.ReferenceWrongCollection)
                            : null;

                if (problem is not null)
                {
                    byField[field.Id] = problem;
                    break;
                }
            }
        }
    }
}

/// <summary>Media kinds through 014's <see cref="MediaKinds"/>.</summary>
internal sealed class ItemMediaLookup(MediaKinds kinds) : IItemMediaLookup
{
    public Task<Func<Guid, MediaKind>> LookupAsync(Guid siteId, IEnumerable<JsonNode?> values, CancellationToken cancellationToken) =>
        kinds.LookupAsync(siteId, values, cancellationToken);
}

/// <summary>Referenced items' collections, read from the site's items.</summary>
internal sealed class ItemReferenceLookup(CmsDbContext database) : IItemReferenceLookup
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> CollectionsOfAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var ids = itemIds.ToList();
        return await database.ForSite<Item>(siteId)
            .AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.CollectionId })
            .ToDictionaryAsync(i => i.Id, i => i.CollectionId, cancellationToken)
            .ConfigureAwait(false);
    }
}
