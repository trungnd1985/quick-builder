using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>What a save would do to a collection's items, or the change that may not be made.</summary>
/// <param name="Impact">Every change with its rule, and counts and examples under each field's main change.</param>
/// <param name="Forbidden">A forbidden change whose field holds values in some item; the save is refused.</param>
internal sealed record ImpactResult(ImpactSummary Impact, FieldChange? Forbidden);

/// <summary>
/// Counts, for each field a save touches, what happens to every item's draft and published
/// value (research CS-07, CS-08, FR-023).
/// </summary>
/// <remarks>
/// Reads without locking, so a preview never blocks a save. The save runs it again under
/// the collection's lock and compares digests. Each item is counted once per field, on the
/// worse of its two copies, through the same <see cref="ValueMigration"/> the migration uses.
/// </remarks>
internal sealed class ImpactCalculator(CmsDbContext database, ValueMigration migration, MediaKinds mediaKinds)
{
    public async Task<ImpactResult> ComputeAsync(
        Collection collection,
        IReadOnlyList<FieldChange> changes,
        IReadOnlyDictionary<string, JsonNode> fillIns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(fillIns);

        var plans = FieldPlan.For(changes, fillIns);
        var itemCount = await database.ForSite<Item>(collection.SiteId)
            .CountAsync(i => i.CollectionId == collection.Id, cancellationToken)
            .ConfigureAwait(false);

        var counts = plans.ToDictionary(p => p.FieldId, _ => new Dictionary<ValueOutcome, int>());
        var examples = plans.ToDictionary(p => p.FieldId, _ => new List<ImpactExample>());
        FieldChange? forbidden = null;

        if (plans.Count > 0 && itemCount > 0)
        {
            var items = await database.ForSite<Item>(collection.SiteId)
                .AsNoTracking()
                .Where(i => i.CollectionId == collection.Id)
                .OrderBy(i => i.Id)
                .Select(i => new { i.Id, i.Draft, i.Published })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var values = items
                .Select(i => (i.Id, Draft: ItemValues.Read(i.Draft), Published: i.Published is null ? null : ItemValues.Read(i.Published)))
                .ToList();

            var media = plans.Any(p => p.InvolvesMedia)
                ? await mediaKinds.LookupAsync(
                    collection.SiteId,
                    values.SelectMany(v => plans.Where(p => p.InvolvesMedia).SelectMany(p => new[] { v.Draft.Get(p.FieldId), v.Published?.Get(p.FieldId) })),
                    cancellationToken).ConfigureAwait(false)
                : static _ => MediaKind.Missing;

            var titleId = collection.SchemaDocument.TitleFieldId;

            foreach (var (id, draft, published) in values)
            {
                foreach (var plan in plans)
                {
                    var draftValue = draft.Get(plan.FieldId);
                    var publishedValue = published?.Get(plan.FieldId);

                    if (plan.Rule == ChangeRule.Forbidden)
                    {
                        if ((draftValue is not null || publishedValue is not null) && forbidden is null)
                        {
                            forbidden = plan.Primary;
                        }

                        continue;
                    }

                    var outcome = ValueMigration.Worse(
                        migration.Apply(plan, draftValue, media)?.Outcome,
                        published is null ? null : migration.Apply(plan, publishedValue, media)?.Outcome);

                    if (outcome is not { } counted)
                    {
                        continue;
                    }

                    counts[plan.FieldId][counted] = counts[plan.FieldId].GetValueOrDefault(counted) + 1;
                    if (counted != ValueOutcome.Kept && examples[plan.FieldId].Count < CollectionSchemaLimits.MaxPreviewExamples)
                    {
                        var title = titleId is null ? null : draft.Get(titleId) is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
                        examples[plan.FieldId].Add(new ImpactExample(id, title, counted));
                    }
                }
            }
        }

        var primaries = plans.ToDictionary(p => p.Primary, p => p);
        var impacts = changes
            .Select(change =>
            {
                var rule = FieldConversion.Classify(change, change.FieldId is { } fieldId && fillIns.ContainsKey(fieldId));
                var impact = new ChangeImpact(change, rule) { FillInAllowed = AllowsFillIn(change) };
                return primaries.TryGetValue(change, out var plan)
                    ? impact with { Counts = counts[plan.FieldId], Examples = examples[plan.FieldId] }
                    : impact;
            })
            .ToList();

        return new ImpactResult(new ImpactSummary(impacts, itemCount), forbidden);
    }

    /// <summary>A fill-in may be given when a field is added, or turned required (FR-029, FR-031).</summary>
    public static bool AllowsFillIn(FieldChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return change.After is { } after
            && after.Type.TakesFillIn()
            && (change.Kind == ChangeKind.FieldAdded || (change.Kind == ChangeKind.RequiredChanged && after.Required));
    }
}
