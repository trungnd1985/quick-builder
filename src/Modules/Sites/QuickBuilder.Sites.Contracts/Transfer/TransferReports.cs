namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>A subject of a check or report line: a kind, an id and a label for the Administrator.</summary>
/// <remarks>Labels are titles, names or addresses. They are kept in the job only, never logged.</remarks>
public sealed record TransferSubject(string Kind, Guid Id, string Label);

/// <summary>What a part reports while checking an archive (data-model §3).</summary>
public interface ICheckReport
{
    /// <summary>A problem that refuses the archive (FR-014).</summary>
    void Error(string code, TransferSubject? subject, string detail);

    /// <summary>A problem the Administrator must accept (FR-015, FR-020, FR-029).</summary>
    void Warning(string code, TransferSubject? subject, string detail);

    /// <summary>How many of <paramref name="kind"/> the archive holds.</summary>
    void Count(string kind, long count);

    /// <summary>A content contract whose documents will be upgraded (research ST-21).</summary>
    void Upgrade(string contract, int archiveVersion, int currentVersion);

    /// <summary>A host name that is used by another site and will not be added (FR-020).</summary>
    void HostNameLeftOut(string hostName);

    /// <summary>A document of the archive that matches one in the target (FR-022, FR-022a).</summary>
    void Conflict(TransferSubject archive, Guid targetId, MatchedBy matchedBy, TransferSubject? clash = null);

    /// <summary>A collection field change a replace or restore will make, with its value counts (FR-024).</summary>
    void SchemaChange(Guid collectionId, string field, string change, long valuesConverted, long valuesLost);

    /// <summary>A target document a restore removes (FR-027b).</summary>
    void Removal(TransferSubject target);

    /// <summary>A document a restore brings back.</summary>
    void Return(TransferSubject archive);

    /// <summary>A document whose content a restore changes.</summary>
    void Change(TransferSubject archive);

    /// <summary>A schedule whose time has passed; it is dropped (FR-029).</summary>
    void PastSchedule(TransferSubject subject, DateTimeOffset atUtc, string action);
}

/// <summary>What a part reports while applying (data-model §4).</summary>
public interface IImportReport
{
    void Imported(string kind, long count);

    /// <summary>How a conflicting document was resolved.</summary>
    void Resolved(TransferSubject archive, Guid targetId, IdOutcome outcome, string? newLabel = null);

    /// <summary>Something of the archive that was not imported, and why.</summary>
    void LeftOut(TransferSubject subject, string reason);

    /// <summary>A reference that points to something neither the archive nor the target holds.</summary>
    void Broken(TransferSubject owner, string missingKind, Guid missingId);

    void DroppedSchedule(TransferSubject subject, DateTimeOffset atUtc);

    /// <summary>How many of <paramref name="kind"/> a restore removed.</summary>
    void Removed(string kind, long count);
}
