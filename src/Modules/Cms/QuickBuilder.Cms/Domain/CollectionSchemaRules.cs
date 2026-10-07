namespace QuickBuilder.Cms.Domain;

/// <summary>Keys kept for item properties that #15 and #16 expose next to fields (contract §4).</summary>
internal static class ReservedKeys
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "slug", "status", "created-at", "updated-at", "published-at", "collection", "site", "schema-version",
    };
}

/// <summary>Whether a document is stored, or proposed by a client (contract §5, §6).</summary>
internal enum SchemaDocumentKind
{
    /// <summary>As kept in the database: permanent ids only, title field designated and required.</summary>
    Stored,

    /// <summary>As sent by the builder: may hold temporary ids, and is normalised before the rules run.</summary>
    Proposal,
}

/// <summary>A broken rule: its name from contract §5 and a JSON Pointer to where.</summary>
internal sealed record RuleFailure(string Rule, string Pointer, string Message);

/// <summary>
/// The rules of the collection schema that its JSON Schema cannot say (contract §5), in their
/// stated order, each over every field before the next.
/// </summary>
/// <remarks>
/// The admin's <c>collectionSchemaRules.ts</c> is a port of this class, and both run the
/// contract's golden files (SC-006), so a change here needs the same change there. Messages
/// never quote names, keys or labels (Principle IX).
/// </remarks>
internal static class CollectionSchemaRules
{
    public static RuleFailure? Check(CollectionSchemaDocument document, SchemaDocumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = document.Fields;

        var keys = new HashSet<string>(Fold.Comparer);
        for (var i = 0; i < fields.Count; i++)
        {
            if (!keys.Add(fields[i].Key))
            {
                return new RuleFailure("duplicate_key", $"/fields/{i}/key", "Two fields have the same key.");
            }
        }

        for (var i = 0; i < fields.Count; i++)
        {
            if (ReservedKeys.All.Contains(fields[i].Key))
            {
                return new RuleFailure("reserved_key", $"/fields/{i}/key", "This key is reserved for item properties.");
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (!ids.Add(fields[i].Id))
            {
                return new RuleFailure("duplicate_field_id", $"/fields/{i}/id", "Two fields have the same id.");
            }
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var labels = new HashSet<string>(Fold.Comparer);
            for (var j = 0; j < fields[i].Options.Count; j++)
            {
                if (!labels.Add(fields[i].Options[j].Label))
                {
                    return new RuleFailure("duplicate_option_label", $"/fields/{i}/options/{j}/label", "Two options of the field have the same label.");
                }
            }
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var j = 0; j < fields[i].Options.Count; j++)
            {
                if (!optionIds.Add(fields[i].Options[j].Id))
                {
                    return new RuleFailure("duplicate_option_id", $"/fields/{i}/options/{j}/id", "Two options of the field have the same id.");
                }
            }
        }

        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i] is { Min: { } min, Max: { } max } && min > max)
            {
                return new RuleFailure("min_above_max", $"/fields/{i}/min", "The minimum is greater than the maximum.");
            }
        }

        if (document.TitleFieldId is { } titleId
            && document.Find(titleId) is not { IsSingleLineText: true, Required: true })
        {
            return new RuleFailure("invalid_title_field", "/titleFieldId", "The field that names items must be a required single-line text field.");
        }

        if (kind == SchemaDocumentKind.Stored)
        {
            if (document.TitleFieldId is null && fields.Any(f => f.IsSingleLineText))
            {
                return new RuleFailure("missing_title_field", "/titleFieldId", "A single-line text field must name the items.");
            }

            for (var i = 0; i < fields.Count; i++)
            {
                if (SchemaIds.IsTemporary(fields[i].Id))
                {
                    return new RuleFailure("temporary_id_in_stored", $"/fields/{i}/id", "A stored schema holds only permanent ids.");
                }

                for (var j = 0; j < fields[i].Options.Count; j++)
                {
                    if (SchemaIds.IsTemporary(fields[i].Options[j].Id))
                    {
                        return new RuleFailure("temporary_id_in_stored", $"/fields/{i}/options/{j}/id", "A stored schema holds only permanent ids.");
                    }
                }
            }
        }

        return null;
    }
}
