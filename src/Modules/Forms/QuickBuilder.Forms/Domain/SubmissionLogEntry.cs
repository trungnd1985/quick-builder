using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// One recorded export, deletion, restore or sweep (018 FR-030, FR-016a, FR-023a; research
/// FS-20; data-model §1, <c>submission_log</c>).
/// </summary>
/// <remarks>
/// Append-only. Counts, ids and filters only: never a submitted value, and for exports never
/// the search words, which may themselves be personal data (FR-022).
/// </remarks>
internal sealed class SubmissionLogEntry : ISiteOwned
{
    public SubmissionLogEntry(Guid siteId, DateTimeOffset atUtc, Guid? actorUserId, string action, string? formId, int count, string? filters)
    {
        SiteId = siteId;
        AtUtc = atUtc;
        ActorUserId = actorUserId;
        Action = action;
        FormId = formId;
        Count = count;
        Filters = filters;
    }

    public long Id { get; private set; }

    public Guid SiteId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    /// <summary>Null for the system (sweeps).</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>One of <see cref="SubmissionLogActions"/>.</summary>
    public string Action { get; private set; }

    public string? FormId { get; private set; }

    public int Count { get; private set; }

    /// <summary>Exports only, as JSON: the filters, with <c>searchUsed</c> instead of the words.</summary>
    public string? Filters { get; private set; }
}

/// <summary>What <see cref="SubmissionLogEntry"/> records.</summary>
internal static class SubmissionLogActions
{
    public const string Export = "export";

    public const string Delete = "delete";

    public const string Restore = "restore";

    public const string RetentionPurge = "retention_purge";

    public const string SpamPurge = "spam_purge";
}
