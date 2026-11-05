using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Domain;

/// <summary>
/// One export, upload or import of a site (020 data-model §1, research ST-15).
/// </summary>
/// <remarks>
/// The aggregate owns the state machine of data-model §1.1. A worker acts on a job only while
/// it holds the job's lease, renewed every 15 seconds; a lease that runs out means the instance
/// died, so its import has rolled back with its connection and the job ends
/// <see cref="TransferFailures.Interrupted"/>.
/// <para>
/// Labels (titles, names) live only in the JSON documents of the check result, summary and
/// report, which only Administrators see. Nothing here is logged.
/// </para>
/// </remarks>
[InstallationScoped("Transfers are Administrator-only installation operations; a new-site import has no site until it commits (020 research ST-15).")]
internal sealed class TransferJob
{
    public const int MaxSiteNameLength = Site.MaxNameLength;

    public static readonly TimeSpan LeaseLength = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ExportRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan UploadRetention = TimeSpan.FromHours(24);
    public static readonly TimeSpan RowRetention = TimeSpan.FromDays(90);

    private TransferJob(Guid id, string kind, string trigger, Guid? startedBy, DateTimeOffset nowUtc)
    {
        Id = id;
        Kind = kind;
        Trigger = trigger;
        StartedBy = startedBy;
        CreatedAtUtc = nowUtc;
        ProgressAtUtc = nowUtc;
        State = TransferStates.Queued;
    }

    // EF Core materialisation.
    private TransferJob()
    {
        Kind = TransferKinds.Export;
        Trigger = TransferTriggers.Admin;
        State = TransferStates.Queued;
    }

    public Guid Id { get; private set; }

    public string Kind { get; private set; }

    public string Trigger { get; private set; }

    /// <summary>Import only: <c>new_site</c>, <c>merge</c> or <c>restore</c>.</summary>
    public string? Mode { get; private set; }

    public bool StartFresh { get; private set; }

    /// <summary>Merge only: <c>replace</c>, <c>skip</c> or <c>rename</c>.</summary>
    public string? ConflictPolicy { get; private set; }

    /// <summary>Export: the source. Merge and restore: the target. New site: set when the import commits.</summary>
    public Guid? SiteId { get; private set; }

    public string? NewSiteName { get; private set; }

    /// <summary>
    /// New site: a host name the Administrator adds, needed when every host name of the archive
    /// is already used here (FR-020; importing the same archive twice on one installation).
    /// </summary>
    public string? NewSiteHostName { get; private set; }

    public string State { get; private set; }

    public string? Step { get; private set; }

    public short Percent { get; private set; }

    public DateTimeOffset ProgressAtUtc { get; private set; }

    public string? ArchiveKey { get; private set; }

    public long? ArchiveBytes { get; private set; }

    public DateTimeOffset? ArchiveExpiresAtUtc { get; private set; }

    /// <summary>Import: the archive's manifest, as JSON, once its integrity is checked.</summary>
    public string? Manifest { get; private set; }

    /// <summary>Import: data-model §3, as JSON.</summary>
    public string? CheckResult { get; private set; }

    /// <summary>Hash of the id map's matches and outcomes the check found (research ST-06).</summary>
    public string? CheckFingerprint { get; private set; }

    /// <summary>When the check finished; a confirmation must follow within 24 hours.</summary>
    public DateTimeOffset? CheckedAtUtc { get; private set; }

    /// <summary>Export: counts and problems (FR-009), as JSON.</summary>
    public string? Summary { get; private set; }

    /// <summary>Import: data-model §4, as JSON.</summary>
    public string? Report { get; private set; }

    public string? FailureCode { get; private set; }

    /// <summary>Restore: the export of the target's state made first (FR-027d).</summary>
    public Guid? PreRestoreJobId { get; private set; }

    public bool CancelRequested { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseUntilUtc { get; private set; }

    /// <summary>Null for the operator command.</summary>
    public Guid? StartedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>PostgreSQL's <c>xmin</c>: two instances never move one job at once.</summary>
    public uint Version { get; private set; }

    public bool IsFinal => TransferStates.Final.Contains(State);

    /// <summary>
    /// Whether the job holds its site's one active slot (FR-010; data-model §1, analysis I1, I2):
    /// an export from queued to finished, an import from confirmed to finished. A check holds
    /// none, and neither does the pre-restore export, whose restore already holds it.
    /// </summary>
    public bool HoldsSiteSlot =>
        SiteId is not null
        && Trigger != TransferTriggers.PreRestore
        && (Kind == TransferKinds.Export
            ? State is TransferStates.Queued or TransferStates.Running
            : State is TransferStates.Confirmed or TransferStates.Running);

    public ImportMode? ImportMode => Mode switch
    {
        TransferModes.NewSite => Contracts.Transfer.ImportMode.NewSite,
        TransferModes.Merge => Contracts.Transfer.ImportMode.Merge,
        TransferModes.Restore => Contracts.Transfer.ImportMode.Restore,
        _ => null,
    };

    public ConflictPolicy? Policy => ConflictPolicy switch
    {
        TransferPolicies.Replace => Contracts.Transfer.ConflictPolicy.Replace,
        TransferPolicies.Skip => Contracts.Transfer.ConflictPolicy.Skip,
        TransferPolicies.Rename => Contracts.Transfer.ConflictPolicy.Rename,
        _ => null,
    };

    // --- Creation --------------------------------------------------------------------------

    /// <summary>An export of <paramref name="siteId"/>, queued for a worker (FR-006).</summary>
    public static TransferJob StartExport(Guid siteId, string trigger, Guid? startedBy, DateTimeOffset nowUtc) =>
        new(Guid.CreateVersion7(), TransferKinds.Export, trigger, startedBy, nowUtc) { SiteId = siteId };

    /// <summary>A reserved upload; the archive's bytes follow (ST-17).</summary>
    public static TransferJob ReserveUpload(string trigger, Guid? startedBy, DateTimeOffset nowUtc) =>
        new(Guid.CreateVersion7(), TransferKinds.Import, trigger, startedBy, nowUtc) { State = TransferStates.Uploading };

    // --- Upload and check ------------------------------------------------------------------

    /// <summary>The archive's bytes are stored under <paramref name="key"/>.</summary>
    public void Uploaded(string key, long bytes, DateTimeOffset nowUtc)
    {
        Require(TransferStates.Uploading);
        ArchiveKey = key;
        ArchiveBytes = bytes;
        ArchiveExpiresAtUtc = nowUtc + UploadRetention;
        State = TransferStates.Uploaded;
        Touch(nowUtc);
    }

    /// <summary>
    /// Queues a check of the stored archive for <paramref name="mode"/>. A refused or expired
    /// check may not be repeated; an unconfirmed one may, against another mode or target.
    /// </summary>
    public Result RequestCheck(string mode, Guid? targetSiteId, DateTimeOffset nowUtc)
    {
        if (Kind != TransferKinds.Import || State is not (TransferStates.Uploaded or TransferStates.AwaitingConfirmation))
        {
            return Result.Failure(TransferErrors.NotCheckable);
        }

        if (!TransferModes.All.Contains(mode))
        {
            return Result.Failure(TransferErrors.InvalidOptions("Unknown import mode."));
        }

        if ((mode == TransferModes.NewSite) != (targetSiteId is null))
        {
            return Result.Failure(TransferErrors.InvalidOptions(
                mode == TransferModes.NewSite ? "A new site has no target site." : "A merge or restore needs a target site."));
        }

        Mode = mode;
        SiteId = targetSiteId;
        CheckResult = null;
        CheckFingerprint = null;
        CheckedAtUtc = null;
        State = TransferStates.Queued;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>The check ended. Errors refuse the archive; otherwise it waits for confirmation (ST-06).</summary>
    public void Checked(string manifest, string checkResult, string fingerprint, bool refused, DateTimeOffset nowUtc)
    {
        Require(TransferStates.Checking);
        Manifest = manifest;
        CheckResult = checkResult;
        CheckFingerprint = fingerprint;
        CheckedAtUtc = nowUtc;
        ReleaseLease();
        State = refused ? TransferStates.Refused : TransferStates.AwaitingConfirmation;
        if (refused)
        {
            FinishedAtUtc = nowUtc;
        }

        Touch(nowUtc);
    }

    /// <summary>The archive could not be read at all (unreadable, integrity, unsafe paths).</summary>
    public void RefusedUnreadable(string checkResult, DateTimeOffset nowUtc)
    {
        Require(TransferStates.Checking);
        CheckResult = checkResult;
        CheckedAtUtc = nowUtc;
        ReleaseLease();
        State = TransferStates.Refused;
        FinishedAtUtc = nowUtc;
        Touch(nowUtc);
    }

    /// <summary>
    /// Confirms the checked import with the Administrator's options (FR-011). The caller has
    /// verified a restore's typed name and the warnings acceptance against the check result.
    /// </summary>
    public Result Confirm(ImportOptions options, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (State != TransferStates.AwaitingConfirmation)
        {
            return Result.Failure(TransferErrors.NotConfirmable);
        }

        if (CheckedAtUtc is not { } checkedAt || nowUtc - checkedAt > UploadRetention)
        {
            return Result.Failure(TransferErrors.CheckExpired);
        }

        if (options.CheckFingerprint is { } seen && seen != CheckFingerprint)
        {
            return Result.Failure(TransferErrors.TargetChanged);
        }

        switch (Mode)
        {
            case TransferModes.NewSite:
                var name = options.NewSiteName?.Trim();
                if (string.IsNullOrEmpty(name) || name.Length > MaxSiteNameLength)
                {
                    return Result.Failure(TransferErrors.InvalidOptions($"A new site needs a name of 1 to {MaxSiteNameLength} characters."));
                }

                if (options.ConflictPolicy is not null)
                {
                    return Result.Failure(TransferErrors.InvalidOptions("A conflict policy applies to a merge only."));
                }

                if (options.NewSiteHostName is { Length: > 0 } host && !QuickBuilder.Shared.Kernel.MultiSite.HostNames.TryNormalise(host, out _))
                {
                    return Result.Failure(TransferErrors.InvalidOptions("The new site's host name is not a valid host name."));
                }

                NewSiteName = name;
                NewSiteHostName = string.IsNullOrWhiteSpace(options.NewSiteHostName) ? null : options.NewSiteHostName.Trim();
                StartFresh = options.StartFresh;
                break;

            case TransferModes.Merge:
                if (options.StartFresh)
                {
                    return Result.Failure(TransferErrors.InvalidOptions("Start fresh applies to a new site only."));
                }

                if (options.ConflictPolicy is not { } policy || !TransferPolicies.All.Contains(policy))
                {
                    return Result.Failure(TransferErrors.InvalidOptions("A merge needs a conflict policy: replace, skip or rename."));
                }

                ConflictPolicy = policy;
                break;

            case TransferModes.Restore:
                if (options.StartFresh || options.ConflictPolicy is not null)
                {
                    return Result.Failure(TransferErrors.InvalidOptions("A restore takes neither start fresh nor a conflict policy."));
                }

                break;

            default:
                return Result.Failure(TransferErrors.NotConfirmable);
        }

        State = TransferStates.Confirmed;
        ConfirmedAtUtc = nowUtc;
        Touch(nowUtc);
        return Result.Success();
    }

    // --- Running ----------------------------------------------------------------------------

    /// <summary>Whether a worker may claim the job now.</summary>
    public bool IsClaimable => State is TransferStates.Queued or TransferStates.Confirmed && !CancelRequested;

    /// <summary>A worker takes the job: an export runs, a queued import is checked, a confirmed one applied.</summary>
    public void Claim(string owner, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!IsClaimable)
        {
            throw new InvalidOperationException($"A transfer in state '{State}' cannot be claimed.");
        }

        State = Kind == TransferKinds.Import && State == TransferStates.Queued ? TransferStates.Checking : TransferStates.Running;
        LeaseOwner = owner;
        LeaseUntilUtc = nowUtc + LeaseLength;
        Percent = 0;
        Step = null;
        Touch(nowUtc);
    }

    /// <summary>Extends the lease of the owner that holds it. False when another owner or no lease holds it.</summary>
    public bool RenewLease(string owner, DateTimeOffset nowUtc)
    {
        if (!TransferStates.Leased.Contains(State) || LeaseOwner != owner)
        {
            return false;
        }

        LeaseUntilUtc = nowUtc + LeaseLength;
        return true;
    }

    public void ReportProgress(string step, double fraction, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        Step = step;
        Percent = (short)Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
        Touch(nowUtc);
    }

    /// <summary>A restore's pre-restore export was created (FR-027d).</summary>
    public void PreRestoreStarted(Guid exportJobId, DateTimeOffset nowUtc)
    {
        Require(TransferStates.Running);
        PreRestoreJobId = exportJobId;
        Step = TransferSteps.PreRestore;
        Touch(nowUtc);
    }

    public void ExportSucceeded(string archiveKey, long archiveBytes, string summary, DateTimeOffset nowUtc)
    {
        Require(TransferStates.Running);
        if (Kind != TransferKinds.Export)
        {
            throw new InvalidOperationException("Only an export has an archive to keep.");
        }

        ArchiveKey = archiveKey;
        ArchiveBytes = archiveBytes;
        ArchiveExpiresAtUtc = nowUtc + ExportRetention;
        Summary = summary;
        Finish(TransferStates.Succeeded, nowUtc);
    }

    /// <summary>The import committed into <paramref name="siteId"/>; its upload is no longer needed.</summary>
    public void ImportSucceeded(Guid siteId, string report, DateTimeOffset nowUtc)
    {
        Require(TransferStates.Running);
        SiteId = siteId;
        Report = report;
        ArchiveExpiresAtUtc = nowUtc;
        Finish(TransferStates.Succeeded, nowUtc);
    }

    public void Failed(string failureCode, DateTimeOffset nowUtc, string? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (IsFinal)
        {
            return;
        }

        FailureCode = failureCode;
        Report = report ?? Report;
        Finish(TransferStates.Failed, nowUtc);
    }

    /// <summary>
    /// Asks the job to stop (FR-007). A job no worker holds stops at once; a running one stops at
    /// its next batch, and an import rolls back (FR-018).
    /// </summary>
    public Result RequestCancel(DateTimeOffset nowUtc)
    {
        if (IsFinal)
        {
            return Result.Failure(TransferErrors.AlreadyFinished);
        }

        if (TransferStates.Leased.Contains(State))
        {
            CancelRequested = true;
            Touch(nowUtc);
        }
        else
        {
            Finish(TransferStates.Cancelled, nowUtc);
        }

        return Result.Success();
    }

    /// <summary>The worker saw the cancel request and stopped.</summary>
    public void Cancelled(DateTimeOffset nowUtc)
    {
        if (!IsFinal)
        {
            Finish(TransferStates.Cancelled, nowUtc);
        }
    }

    /// <summary>Fails a job whose worker's lease ran out (ST-15). True when it changed.</summary>
    public bool FailIfLeaseExpired(DateTimeOffset nowUtc)
    {
        if (!TransferStates.Leased.Contains(State) || LeaseUntilUtc is not { } until || until >= nowUtc)
        {
            return false;
        }

        FailureCode = TransferFailures.Interrupted;
        Finish(TransferStates.Failed, nowUtc);
        return true;
    }

    // --- Archive lifetime ----------------------------------------------------------------------

    /// <summary>The stored archive was deleted, by an Administrator or by retention.</summary>
    public void ArchiveDeleted(DateTimeOffset nowUtc)
    {
        ArchiveKey = null;
        ArchiveExpiresAtUtc = null;
        if (State is TransferStates.Succeeded && Kind == TransferKinds.Export)
        {
            State = TransferStates.Expired;
        }
        else if (State is TransferStates.Uploading or TransferStates.Uploaded or TransferStates.AwaitingConfirmation)
        {
            Finish(TransferStates.Expired, nowUtc);
        }

        Touch(nowUtc);
    }

    /// <summary>Whether the archive can be downloaded now (FR-008).</summary>
    public bool IsDownloadable(DateTimeOffset nowUtc) =>
        Kind == TransferKinds.Export && State == TransferStates.Succeeded && ArchiveKey is not null
        && ArchiveExpiresAtUtc is { } expires && expires > nowUtc;

    // --- Helpers --------------------------------------------------------------------------------

    private void Finish(string state, DateTimeOffset nowUtc)
    {
        State = state;
        FinishedAtUtc = nowUtc;
        ReleaseLease();
        Touch(nowUtc);
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }

    private void Touch(DateTimeOffset nowUtc) => ProgressAtUtc = nowUtc;

    private void Require(string state)
    {
        if (State != state)
        {
            throw new InvalidOperationException($"The transfer is '{State}', not '{state}'.");
        }
    }
}

/// <summary>Import modes as stored.</summary>
internal static class TransferModes
{
    public const string NewSite = "new_site";
    public const string Merge = "merge";
    public const string Restore = "restore";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { NewSite, Merge, Restore };
}

/// <summary>Conflict policies as stored.</summary>
internal static class TransferPolicies
{
    public const string Replace = "replace";
    public const string Skip = "skip";
    public const string Rename = "rename";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Replace, Skip, Rename };
}

/// <summary>The Administrator's options when confirming an import (transfer-api.yaml confirmImport).</summary>
internal sealed record ImportOptions(
    string? NewSiteName,
    bool StartFresh,
    string? ConflictPolicy,
    string? CheckFingerprint,
    string? NewSiteHostName = null);

/// <summary>Errors of the transfer state machine (data-model §5).</summary>
internal static class TransferErrors
{
    public static readonly Error NotFound = Error.NotFound("transfer.not_found", "No such transfer.");
    public static readonly Error Busy = Error.Conflict("transfer.busy", "This site already has an export or import running. Try again when it has finished.");
    public static readonly Error NotCheckable = Error.Conflict("transfer.not_checkable", "This transfer has no archive waiting to be checked.");
    public static readonly Error NotConfirmable = Error.Conflict("transfer.not_confirmable", "This transfer is not waiting for confirmation.");
    public static readonly Error CheckExpired = Error.Conflict("transfer.check_expired", "The check is more than 24 hours old. Upload the archive and check it again.");
    public static readonly Error TargetChanged = Error.Conflict("transfer.target_changed", "The site changed since the check. Check the archive again before importing.");
    public static readonly Error AlreadyFinished = Error.Conflict("transfer.finished", "This transfer has already finished.");
    public static readonly Error Changed = Error.Conflict("transfer.changed", "The transfer changed at the same time. Try again.");
    public static readonly Error ArchiveGone = Error.Conflict("transfer.archive_gone", "The archive has expired or was deleted.");
    public static readonly Error TooLarge = Error.Validation("transfer.too_large", "The archive is larger than this installation accepts as an upload. Use the 'sites import' command instead.");
    public static readonly Error RestoreNotAllowed = Error.Conflict("transfer.restore_not_allowed", "This archive was not exported from this site, so it cannot restore it.");
    public static readonly Error ConfirmationMismatch = Error.Validation("transfer.confirmation_mismatch", "Type the site's name exactly to confirm the restore.");
    public static readonly Error WarningsNotAccepted = Error.Validation("transfer.warnings_not_accepted", "The check found warnings. Accept them to import.");

    public static Error InvalidOptions(string message) => Error.Validation("transfer.invalid_options", message);
}
