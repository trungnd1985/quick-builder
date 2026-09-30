using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// One run of the storage consistency check (013 data-model.md §1.5, research MV-21).
/// </summary>
/// <remarks>
/// Installation-level by nature: it describes the configured storage provider, not a site,
/// so it carries no site id. Only <c>media.storage.check</c> holders (Administrators) read it.
/// </remarks>
internal sealed class StorageCheck
{
    /// <summary>How many missing files a run lists. The count is always exact.</summary>
    public const int MaxListedMissing = 1_000;

    private StorageCheck()
    {
        Trigger = string.Empty;
        Provider = string.Empty;
        Status = string.Empty;
    }

    public StorageCheck(Guid id, string trigger, string provider, DateTimeOffset startedAtUtc, DateTimeOffset leaseUntilUtc, Guid? startedBy)
    {
        Id = id;
        Trigger = trigger;
        Provider = provider;
        Status = StorageCheckStatus.Running;
        StartedAtUtc = startedAtUtc;
        LeaseUntilUtc = leaseUntilUtc;
        StartedBy = startedBy;
    }

    public Guid Id { get; private set; }

    /// <summary>One of <see cref="StorageCheckTrigger"/>.</summary>
    public string Trigger { get; private set; }

    /// <summary>The configured provider when the run started.</summary>
    public string Provider { get; private set; }

    /// <summary>One of <see cref="StorageCheckStatus"/>.</summary>
    public string Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    public DateTimeOffset LeaseUntilUtc { get; private set; }

    public Guid? StartedBy { get; private set; }

    public int FilesChecked { get; private set; }

    public int MissingCount { get; private set; }

    internal void Progress(int filesChecked, int missingCount, DateTimeOffset leaseUntilUtc)
    {
        FilesChecked = filesChecked;
        MissingCount = missingCount;
        LeaseUntilUtc = leaseUntilUtc;
    }

    internal void Finish(string status, DateTimeOffset finishedAtUtc)
    {
        Status = status;
        FinishedAtUtc = finishedAtUtc;
    }
}

/// <summary>A library file whose current original the provider is missing, found by one check.</summary>
internal sealed class StorageCheckMissing : ISiteOwned
{
    public StorageCheckMissing(Guid checkId, Guid siteId, Guid fileId)
    {
        CheckId = checkId;
        SiteId = siteId;
        FileId = fileId;
    }

    public Guid CheckId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid FileId { get; private set; }
}

/// <summary>What started a check.</summary>
internal static class StorageCheckTrigger
{
    public const string Startup = "startup";
    public const string Admin = "admin";
    public const string Cli = "cli";
}

/// <summary>Where a check is.</summary>
internal static class StorageCheckStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
