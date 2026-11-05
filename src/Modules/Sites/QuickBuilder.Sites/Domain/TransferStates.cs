namespace QuickBuilder.Sites.Domain;

/// <summary>The states of a transfer job (020 data-model §1.1). Stored as these strings.</summary>
internal static class TransferStates
{
    /// <summary>Import: reserved, waiting for the archive's bytes.</summary>
    public const string Uploading = "uploading";

    /// <summary>Import: the archive is stored and can be checked.</summary>
    public const string Uploaded = "uploaded";

    /// <summary>Waiting for a worker: an export to run, or an import to check.</summary>
    public const string Queued = "queued";

    public const string Checking = "checking";

    /// <summary>Import: the check failed; nothing changed.</summary>
    public const string Refused = "refused";

    public const string AwaitingConfirmation = "awaiting_confirmation";

    /// <summary>Import: confirmed and waiting for a worker to apply it.</summary>
    public const string Confirmed = "confirmed";

    public const string Running = "running";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    public const string Cancelled = "cancelled";

    /// <summary>The stored archive is gone (retention or deletion), or a check was never confirmed.</summary>
    public const string Expired = "expired";

    /// <summary>
    /// The SQL predicate of <c>ux_transfer_jobs_active_site</c>, which must say exactly what
    /// <see cref="TransferJob.HoldsSiteSlot"/> says (data-model §1).
    /// </summary>
    public const string HoldingSlotSql =
        "site_id IS NOT NULL AND trigger <> 'pre_restore' AND " +
        "((kind = 'export' AND state IN ('queued', 'running')) OR (kind = 'import' AND state IN ('confirmed', 'running')))";

    /// <summary>The states a worker is acting in, under a lease.</summary>
    public static readonly IReadOnlySet<string> Leased = new HashSet<string>(StringComparer.Ordinal) { Checking, Running };

    /// <summary>States a job never leaves, apart from its archive expiring.</summary>
    public static readonly IReadOnlySet<string> Final = new HashSet<string>(StringComparer.Ordinal) { Refused, Succeeded, Failed, Cancelled, Expired };
}

/// <summary>What a transfer job does.</summary>
internal static class TransferKinds
{
    public const string Export = "export";
    public const string Import = "import";
}

/// <summary>Who started a transfer.</summary>
internal static class TransferTriggers
{
    public const string Admin = "admin";
    public const string Cli = "cli";

    /// <summary>The export a restore makes of the current state first (FR-027d).</summary>
    public const string PreRestore = "pre_restore";
}

/// <summary>The steps a running job reports (data-model §1).</summary>
internal static class TransferSteps
{
    public const string Reading = "reading";
    public const string PreRestore = "pre_restore";
    public const string Committing = "committing";
}

/// <summary>Codes of the reasons a job failed (data-model §5).</summary>
internal static class TransferFailures
{
    public const string Interrupted = "interrupted";
    public const string PreRestoreExportFailed = "pre_restore_export_failed";
    public const string StorageFailed = "storage_failed";
    public const string TargetChanged = "transfer.target_changed";
    public const string Unexpected = "unexpected";
}
