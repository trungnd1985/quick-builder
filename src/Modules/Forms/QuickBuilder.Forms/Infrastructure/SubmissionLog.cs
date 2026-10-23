using QuickBuilder.Forms.Domain;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Records exports, deletions, restores and sweeps (018 FR-030, FR-016a, FR-023a; research
/// FS-20). Counts, ids and filters only, never a value or a search word (FR-022).
/// </summary>
internal sealed class SubmissionLog(FormsDbContext database, TimeProvider clock)
{
    /// <summary>Adds an entry to the context; the caller's save writes it with the change it records.</summary>
    public void Add(Guid siteId, Guid? actor, string action, string? formId, int count, string? filters = null) =>
        database.SubmissionLog.Add(new SubmissionLogEntry(siteId, clock.GetUtcNow(), actor, action, formId, count, filters));
}
