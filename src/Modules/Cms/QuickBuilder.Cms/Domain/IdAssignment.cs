namespace QuickBuilder.Cms.Domain;

/// <summary>
/// Replaces a proposal's temporary ids with permanent ones from the collection's counters
/// (research CS-02, contract §6).
/// </summary>
/// <remarks>
/// Fields get <c>f{n}</c> and options <c>o{n}</c>, in document order, so the same proposal
/// always receives the same ids. The counters only grow, so an id is never issued twice,
/// even after its field is removed (FR-006).
/// </remarks>
internal sealed record IdAssignment(
    CollectionSchemaDocument Document,
    IReadOnlyDictionary<string, string> IdMap,
    int NextFieldNumber,
    int NextOptionNumber)
{
    /// <summary>The permanent id of <paramref name="id"/>: itself when it is not temporary.</summary>
    public string Resolve(string id) => IdMap.TryGetValue(id, out var permanent) ? permanent : id;

    public static IdAssignment Assign(CollectionSchemaDocument proposal, int nextFieldNumber, int nextOptionNumber)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var fields = new List<Field>(proposal.Fields.Count);

        foreach (var field in proposal.Fields)
        {
            var id = field.Id;
            if (SchemaIds.IsTemporary(id))
            {
                id = SchemaIds.Field(nextFieldNumber++);
                map[field.Id] = id;
            }

            var options = new List<FieldOption>(field.Options.Count);
            foreach (var option in field.Options)
            {
                if (SchemaIds.IsTemporary(option.Id))
                {
                    var optionId = SchemaIds.Option(nextOptionNumber++);
                    map[option.Id] = optionId;
                    options.Add(option with { Id = optionId });
                }
                else
                {
                    options.Add(option);
                }
            }

            fields.Add(field with { Id = id, Options = options });
        }

        var document = proposal with
        {
            TitleFieldId = proposal.TitleFieldId is { } title && map.TryGetValue(title, out var titleId) ? titleId : proposal.TitleFieldId,
            Fields = fields,
        };

        return new IdAssignment(document, map, nextFieldNumber, nextOptionNumber);
    }

    /// <summary>
    /// The first id problem in a proposal against the stored schema, or null (contract §5, §6):
    /// a temporary id used twice, or a permanent id the collection does not have.
    /// </summary>
    public static (string Code, string Rule, string Pointer)? FindUnknownOrDuplicate(CollectionSchemaDocument stored, CollectionSchemaDocument proposal)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(proposal);

        var temporary = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < proposal.Fields.Count; i++)
        {
            var field = proposal.Fields[i];
            if (SchemaIds.IsTemporary(field.Id) && !temporary.Add(field.Id))
            {
                return ("cms.invalid_schema", "duplicate_temporary_id", $"/fields/{i}/id");
            }

            for (var j = 0; j < field.Options.Count; j++)
            {
                if (SchemaIds.IsTemporary(field.Options[j].Id) && !temporary.Add(field.Options[j].Id))
                {
                    return ("cms.invalid_schema", "duplicate_temporary_id", $"/fields/{i}/options/{j}/id");
                }
            }
        }

        for (var i = 0; i < proposal.Fields.Count; i++)
        {
            var field = proposal.Fields[i];
            if (SchemaIds.IsTemporary(field.Id))
            {
                continue;
            }

            if (stored.Find(field.Id) is not { } before)
            {
                return ("cms.unknown_field_id", "unknown_field_id", $"/fields/{i}/id");
            }

            for (var j = 0; j < field.Options.Count; j++)
            {
                var optionId = field.Options[j].Id;
                if (!SchemaIds.IsTemporary(optionId) && before.FindOption(optionId) is null)
                {
                    return ("cms.unknown_option_id", "unknown_option_id", $"/fields/{i}/options/{j}/id");
                }
            }
        }

        return null;
    }
}
