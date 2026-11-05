using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Domain;

/// <summary>
/// Where every id an archive declares goes in the target (020 research ST-08, data-model §6).
/// </summary>
/// <remarks>
/// Parts declare their ids during the check; matching decides each outcome; the map is then
/// complete before any part applies. Ids are random UUIDs, so an id of one kind never equals an
/// id of another: the map is keyed by id alone for document remapping, and by kind for lookups.
/// </remarks>
internal sealed class IdMap : IIdMapBuilder, IIdMap
{
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly List<string> _notes = [];
    private readonly ImportMode _mode;
    private readonly ConflictPolicy? _policy;
    private readonly Func<Guid> _newId;

    public IdMap(ImportMode mode, ConflictPolicy? policy, Func<Guid>? newId = null)
    {
        _mode = mode;
        _policy = policy;
        _newId = newId ?? Guid.CreateVersion7;
    }

    public int Count => _entries.Count;

    // --- Building (check) -----------------------------------------------------------------

    public void Declare(string kind, Guid archiveId, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (_entries.ContainsKey(archiveId))
        {
            throw new ArchiveFormatException($"The archive declares the id {archiveId} twice.");
        }

        _entries[archiveId] = _mode == ImportMode.Restore
            ? new Entry(kind, archiveId, IdOutcome.Original, label, null, null)
            : new Entry(kind, _newId(), IdOutcome.New, label, null, null);
    }

    public void Match(string kind, Guid archiveId, Guid targetId, MatchedBy matchedBy)
    {
        var entry = Require(kind, archiveId) with { MatchedTarget = targetId, MatchedBy = matchedBy };
        if (_mode == ImportMode.Restore)
        {
            _entries[archiveId] = entry;
            return;
        }

        // A matched document is replaced or skipped in place; renamed means it is added beside
        // the target's under its new id (research ST-10). A check, which has no policy yet,
        // records the match only.
        if (_policy is not { } policy)
        {
            _entries[archiveId] = entry;
            return;
        }

        _entries[archiveId] = ConflictRules.Outcome(kind, policy) switch
        {
            IdOutcome.Replaced => entry with { TargetId = targetId, Outcome = IdOutcome.Replaced },
            IdOutcome.Skipped => entry with { TargetId = targetId, Outcome = IdOutcome.Skipped },
            _ => entry with { Outcome = IdOutcome.Renamed },
        };
    }

    public void Reuse(string kind, Guid archiveId, Guid targetId)
    {
        var entry = Require(kind, archiveId);
        _entries[archiveId] = entry with { TargetId = targetId, Outcome = IdOutcome.Reused, MatchedTarget = targetId, MatchedBy = Contracts.Transfer.MatchedBy.Content };
    }

    public bool IsDeclared(string kind, Guid archiveId) =>
        _entries.TryGetValue(archiveId, out var entry) && entry.Kind == kind;

    // --- Reading (apply) ------------------------------------------------------------------

    public Guid? Resolve(string kind, Guid archiveId) =>
        _entries.TryGetValue(archiveId, out var entry) && entry.Kind == kind ? entry.TargetId : null;

    public IdOutcome? Outcome(string kind, Guid archiveId) =>
        _entries.TryGetValue(archiveId, out var entry) && entry.Kind == kind ? entry.Outcome : null;

    public Guid Remap(string kind, Guid archiveId, Action<string, Guid>? broken = null)
    {
        if (Resolve(kind, archiveId) is { } target)
        {
            return target;
        }

        broken?.Invoke(kind, archiveId);
        return archiveId;
    }

    /// <summary>
    /// Rewrites every id the archive declares inside a document to its target id (research
    /// ST-09, as built). Any other value, node ids included, is left as it is.
    /// </summary>
    public string RemapDocument(JsonElement document) => RemapNode(JsonNode.Parse(document.GetRawText()))?.ToJsonString() ?? "null";

    private JsonNode? RemapNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    var replaced = RemapNode(property.Value);
                    if (!ReferenceEquals(replaced, property.Value))
                    {
                        obj[property.Key] = replaced;
                    }
                }

                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var replaced = RemapNode(array[i]);
                    if (!ReferenceEquals(replaced, array[i]))
                    {
                        array[i] = replaced;
                    }
                }

                return array;

            case JsonValue value when value.TryGetValue<string>(out var text)
                && Guid.TryParse(text, out var id)
                && _entries.TryGetValue(id, out var entry)
                && entry.TargetId != id:
                // Keep the spelling the document used: "N" for 32 digits, "D" otherwise.
                return JsonValue.Create(text.Length == 32 ? entry.TargetId.ToString("N") : entry.TargetId.ToString());

            default:
                return node;
        }
    }

    // --- Fingerprint and reporting ---------------------------------------------------------

    /// <summary>
    /// A hash of every match and every noted fact about the target, which a confirmation and
    /// the apply compare to know the target did not change since the check (research ST-06).
    /// </summary>
    /// <remarks>
    /// It does not depend on the merge policy, which is chosen after the check, nor on new ids,
    /// which are allocated afresh each time: only on what the target holds that the archive meets.
    /// </remarks>
    public string Fingerprint()
    {
        var text = new StringBuilder();
        foreach (var (archiveId, entry) in _entries.OrderBy(e => e.Key))
        {
            if (entry.MatchedTarget is not { } target)
            {
                continue;
            }

            text.Append(entry.Kind).Append(':').Append(archiveId.ToString("N")).Append('>')
                .Append(entry.MatchedBy).Append(':').Append(target.ToString("N")).Append('\n');
        }

        foreach (var note in _notes.Order(StringComparer.Ordinal))
        {
            text.Append(note).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The ids of <paramref name="kind"/> with <paramref name="outcome"/>, for reports.</summary>
    public IEnumerable<(Guid ArchiveId, Guid TargetId, string Label, MatchedBy? MatchedBy)> Of(string kind, IdOutcome outcome) =>
        _entries.Where(e => e.Value.Kind == kind && e.Value.Outcome == outcome)
            .Select(e => (e.Key, e.Value.TargetId, e.Value.Label, e.Value.MatchedBy));

    /// <summary>Every match the check found, for its report: archive id, kind, label, target and how.</summary>
    public IEnumerable<(Guid ArchiveId, string Kind, string Label, Guid TargetId, MatchedBy MatchedBy)> Matches() =>
        _entries.Where(e => e.Value.MatchedTarget is not null && e.Value.Outcome != IdOutcome.Reused)
            .Select(e => (e.Key, e.Value.Kind, e.Value.Label, e.Value.MatchedTarget!.Value, e.Value.MatchedBy!.Value));

    /// <summary>Adds a fact about the target that the fingerprint must cover, such as a document a restore removes.</summary>
    public void Note(string fact) => _notes.Add(fact);

    private Entry Require(string kind, Guid archiveId) =>
        _entries.TryGetValue(archiveId, out var entry) && entry.Kind == kind
            ? entry
            : throw new InvalidOperationException($"The {kind} {archiveId} was not declared before it was matched.");

    private sealed record Entry(string Kind, Guid TargetId, IdOutcome Outcome, string Label, MatchedBy? MatchedBy, Guid? MatchedTarget);
}
