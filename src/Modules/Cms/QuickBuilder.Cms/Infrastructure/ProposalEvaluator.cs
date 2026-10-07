using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features;
using QuickBuilder.Cms.Infrastructure.Schema;
using QuickBuilder.Pages.Contracts.Schema;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>A proposal checked against a collection: everything a preview shows and a save applies.</summary>
internal sealed record Evaluation(
    CollectionDetails Details,
    IdAssignment Assignment,
    IReadOnlyList<FieldChange> Changes,
    IReadOnlyList<FieldPlan> Plans,
    ImpactSummary Impact);

/// <summary>
/// Checks a proposed definition against a stored collection and works out what saving it
/// would do (research CS-04, CS-06, CS-07). Shared by the preview and the save, so both give
/// exactly the same answers.
/// </summary>
/// <remarks>
/// In order: the details; the schema against the contract; the ids against the collection;
/// permanent ids for the temporary ones; reference targets in the same site; the diff; the
/// fill-in values; the impact on the items, refusing a forbidden change while values exist.
/// </remarks>
internal sealed class ProposalEvaluator(
    CmsDbContext database,
    CollectionSchemaValidator schemaValidator,
    ItemValueValidator valueValidator,
    ImpactCalculator calculator)
{
    public async Task<Result<Evaluation>> EvaluateAsync(Collection collection, SaveCollectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);

        var details = CollectionDetailsRules.Normalise(request.Name, request.Slug, request.Description);
        if (CollectionDetailsRules.Check(details) is { } badDetails)
        {
            return Fail(CmsErrors.Invalid(CollectionSchemaValidator.InvalidSchema, badDetails.Rule, badDetails.Pointer, badDetails.Message));
        }

        if (request.Schema is not { ValueKind: JsonValueKind.Object } schema)
        {
            return Fail(CmsErrors.Invalid(CollectionSchemaValidator.InvalidSchema, "schema_violation", string.Empty, "A schema document is required."));
        }

        var validation = schemaValidator.Validate(Encoding.UTF8.GetBytes(schema.GetRawText()), SchemaDocumentKind.Proposal);
        if (!validation.IsValid)
        {
            return Fail(validation.Code == "cms.proposal_too_large"
                ? CmsErrors.ProposalTooLarge()
                : CmsErrors.Invalid(validation.Code!, validation.Rule!, validation.Pointer!, validation.Message!));
        }

        var proposal = validation.Document!;
        if (IdAssignment.FindUnknownOrDuplicate(collection.SchemaDocument, proposal) is { } idProblem)
        {
            return Fail(CmsErrors.Invalid(idProblem.Code, idProblem.Rule, idProblem.Pointer, "The proposal uses an id this collection has not issued, or a temporary id twice."));
        }

        var assignment = collection.AssignIds(proposal);
        if (await FindUnknownTargetAsync(collection, assignment.Document, cancellationToken).ConfigureAwait(false) is { } targetPointer)
        {
            return Fail(CmsErrors.UnknownTargetCollection(targetPointer));
        }

        var changes = SchemaDiff.Compare(
            collection.Details, collection.SchemaDocument, details, assignment.Document,
            assignment.IdMap.Values.Where(id => id.StartsWith('f')).ToHashSet(StringComparer.Ordinal));

        var fillIns = ResolveFillIns(request.FillIns, assignment, changes, out var fillInError);
        if (fillInError is not null)
        {
            return Fail(fillInError);
        }

        var result = await calculator.ComputeAsync(collection, changes, fillIns, cancellationToken).ConfigureAwait(false);
        if (result.Forbidden is { } forbidden)
        {
            return Fail(forbidden.Kind == ChangeKind.SettingsChanged
                ? CmsErrors.TargetChangeNotAllowed(forbidden.FieldId!)
                : CmsErrors.TypeChangeNotAllowed(forbidden.FieldId!, forbidden.Before!.Type.ToName(), forbidden.After!.Type.ToName()));
        }

        return Result.Success(new Evaluation(details, assignment, changes, FieldPlan.For(changes, fillIns), result.Impact));
    }

    private static Result<Evaluation> Fail(Error error) => Result.Failure<Evaluation>(error);

    /// <summary>The pointer of the first reference field whose target is not a collection of this site.</summary>
    private async Task<string?> FindUnknownTargetAsync(Collection collection, CollectionSchemaDocument schema, CancellationToken cancellationToken)
    {
        var targets = schema.Fields
            .Select(f => f.Target)
            .OfType<Guid>()
            .Where(t => t != collection.Id)
            .Distinct()
            .ToList();

        if (targets.Count == 0)
        {
            return null;
        }

        var known = await database.ForSite<Collection>(collection.SiteId)
            .Where(c => targets.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < schema.Fields.Count; i++)
        {
            if (schema.Fields[i].Target is { } target && target != collection.Id && !known.Contains(target))
            {
                return $"/fields/{i}/target";
            }
        }

        return null;
    }

    /// <summary>
    /// Fill-in values keyed by permanent field id, option values with permanent option ids.
    /// Each must belong to a field added or turned required in this save, of a type that takes
    /// one, and be a valid value for it (FR-029, FR-031).
    /// </summary>
    private Dictionary<string, JsonNode> ResolveFillIns(
        IReadOnlyDictionary<string, JsonElement>? requested,
        IdAssignment assignment,
        IReadOnlyList<FieldChange> changes,
        out Error? error)
    {
        error = null;
        var resolved = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        foreach (var (key, element) in requested ?? new Dictionary<string, JsonElement>())
        {
            var pointer = $"/fillIns/{SchemaSubsetValidator.Escape(key)}";
            var fieldId = assignment.Resolve(key);
            var change = changes.FirstOrDefault(c => c.FieldId == fieldId && ImpactCalculator.AllowsFillIn(c));
            var value = JsonNode.Parse(element.GetRawText());

            if (change?.After is not { } field || value is null)
            {
                error = CmsErrors.InvalidFillIn(fieldId, pointer);
                return resolved;
            }

            if (field.Type == FieldType.Option && value is JsonValue option && option.TryGetValue<string>(out var optionId))
            {
                value = JsonValue.Create(assignment.Resolve(optionId));
            }

            if (valueValidator.Check(field, value) != ValueState.Valid)
            {
                error = CmsErrors.InvalidFillIn(fieldId, pointer);
                return resolved;
            }

            resolved[fieldId] = value;
        }

        return resolved;
    }
}
