using System.Text.Json;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Collects what the parts report while checking (020 data-model §3). Lists keep at most 1,000
/// entries each; <see cref="Totals"/> keeps the full counts.
/// </summary>
internal sealed class CheckReport : ICheckReport
{
    public const int MaxListed = 1000;

    private readonly Dictionary<string, long> _totals = new(StringComparer.Ordinal);

    public List<CheckProblem> Problems { get; } = [];

    public Dictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    public List<CheckVersion> Versions { get; } = [];

    public List<string> HostNamesLeftOut { get; } = [];

    public List<string> HostNamesKept { get; } = [];

    public List<CheckConflict> Conflicts { get; } = [];

    public List<CheckSchemaChange> SchemaChanges { get; } = [];

    public List<TransferSubject> Removals { get; } = [];

    public List<TransferSubject> Returns { get; } = [];

    public List<TransferSubject> Changes { get; } = [];

    public List<CheckSchedule> PastSchedules { get; } = [];

    public IReadOnlyDictionary<string, long> Totals => _totals;

    public bool HasErrors => Problems.Any(p => p.Severity == "error");

    public bool HasWarnings => Problems.Any(p => p.Severity == "warning");

    public void Error(string code, TransferSubject? subject, string detail) => Add(Problems, new CheckProblem("error", code, subject, detail), "problems");

    public void Warning(string code, TransferSubject? subject, string detail) => Add(Problems, new CheckProblem("warning", code, subject, detail), "problems");

    public void Count(string kind, long count) => Counts[kind] = Counts.GetValueOrDefault(kind) + count;

    public void Upgrade(string contract, int archiveVersion, int currentVersion)
    {
        if (!Versions.Any(v => v.Contract == contract))
        {
            Versions.Add(new CheckVersion(contract, archiveVersion, currentVersion));
        }
    }

    public void HostNameLeftOut(string hostName) => Add(HostNamesLeftOut, hostName, "hostNamesLeftOut");

    /// <summary>A host name of the archive that a new site takes.</summary>
    public void HostNameKept(string hostName) => HostNamesKept.Add(hostName);

    public void Conflict(TransferSubject archive, Guid targetId, MatchedBy matchedBy, TransferSubject? clash = null) =>
        Add(Conflicts, new CheckConflict(archive, targetId, matchedBy, clash), "conflicts");

    public void SchemaChange(Guid collectionId, string field, string change, long valuesConverted, long valuesLost) =>
        Add(SchemaChanges, new CheckSchemaChange(collectionId, field, change, valuesConverted, valuesLost), "schemaChanges");

    public void Removal(TransferSubject target) => Add(Removals, target, "removals");

    public void Return(TransferSubject archive) => Add(Returns, archive, "returns");

    public void Change(TransferSubject archive) => Add(Changes, archive, "changes");

    public void PastSchedule(TransferSubject subject, DateTimeOffset atUtc, string action) =>
        Add(PastSchedules, new CheckSchedule(subject, atUtc, action), "pastSchedules");

    private void Add<T>(List<T> list, T item, string name)
    {
        _totals[name] = _totals.GetValueOrDefault(name) + 1;
        if (list.Count < MaxListed)
        {
            list.Add(item);
        }
    }

    public string ToJson(ArchiveManifest? manifest, bool sameSiteAsTarget, IReadOnlyList<string> allowedModes) =>
        JsonSerializer.Serialize(new
        {
            archive = manifest is null ? null : new
            {
                format = manifest.Format,
                release = manifest.Release,
                createdAtUtc = manifest.CreatedAtUtc,
                source = manifest.Source,
                sameSiteAsTarget,
            },
            versions = Versions,
            counts = Counts,
            problems = Problems,
            hostNamesLeftOut = HostNamesLeftOut,
            hostNamesKept = HostNamesKept,
            conflicts = Conflicts,
            schemaChanges = SchemaChanges,
            removals = Removals,
            returns = Returns,
            changes = Changes,
            pastSchedules = PastSchedules,
            allowedModes,
            totals = _totals,
        }, ArchiveJson.Options);
}

internal sealed record CheckProblem(string Severity, string Code, TransferSubject? Subject, string Detail);

internal sealed record CheckVersion(string Contract, int ArchiveVersion, int CurrentVersion);

internal sealed record CheckConflict(TransferSubject Archive, Guid TargetId, MatchedBy MatchedBy, TransferSubject? Clash);

internal sealed record CheckSchemaChange(Guid CollectionId, string Field, string Change, long ValuesConverted, long ValuesLost);

internal sealed record CheckSchedule(TransferSubject Subject, DateTimeOffset AtUtc, string Action);

/// <summary>Collects what the parts report while applying (020 data-model §4).</summary>
internal sealed class ImportReport : IImportReport
{
    public Dictionary<string, long> ImportedCounts { get; } = new(StringComparer.Ordinal);

    public List<object> ResolvedList { get; } = [];

    public List<object> LeftOutList { get; } = [];

    public List<object> BrokenList { get; } = [];

    public List<object> DroppedSchedules { get; } = [];

    public Dictionary<string, long> RemovedCounts { get; } = new(StringComparer.Ordinal);

    public void Imported(string kind, long count) => ImportedCounts[kind] = ImportedCounts.GetValueOrDefault(kind) + count;

    public void Resolved(TransferSubject archive, Guid targetId, IdOutcome outcome, string? newLabel = null) =>
        Add(ResolvedList, new { archive, targetId, outcome, newLabel });

    public void LeftOut(TransferSubject subject, string reason) => Add(LeftOutList, new { subject, reason });

    public void Broken(TransferSubject owner, string missingKind, Guid missingId) => Add(BrokenList, new { owner, missingKind, missingId });

    public void DroppedSchedule(TransferSubject subject, DateTimeOffset atUtc) => Add(DroppedSchedules, new { subject, atUtc });

    public void Removed(string kind, long count) => RemovedCounts[kind] = RemovedCounts.GetValueOrDefault(kind) + count;

    private static void Add(List<object> list, object item)
    {
        if (list.Count < CheckReport.MaxListed)
        {
            list.Add(item);
        }
    }

    public string ToJson(string mode, bool startFresh, string? policy, Guid siteId, Guid? preRestoreTransferId, int historicalAuthors) =>
        JsonSerializer.Serialize(new
        {
            mode,
            startFresh,
            policy,
            siteId,
            imported = ImportedCounts,
            resolved = ResolvedList,
            leftOut = LeftOutList,
            broken = BrokenList,
            droppedSchedules = DroppedSchedules,
            removed = RemovedCounts,
            preRestoreTransferId,
            historicalAuthors,
        }, ArchiveJson.Options);
}

/// <summary>Archive member ids to ids here (research ST-14). An id the archive does not list is kept.</summary>
internal sealed class AuthorMap(IReadOnlyDictionary<Guid, Guid> map) : IAuthorMap
{
    public int Count => map.Count;

    public Guid Map(Guid archiveUserId) => map.GetValueOrDefault(archiveUserId, archiveUserId);

    public Guid? Map(Guid? archiveUserId) => archiveUserId is { } id ? Map(id) : null;
}

/// <summary>Which options of a confirmed new-site import the Sites part needs (scoped to the job).</summary>
internal sealed class NewSiteOptions
{
    public string? Name { get; set; }

    public string? HostName { get; set; }
}
