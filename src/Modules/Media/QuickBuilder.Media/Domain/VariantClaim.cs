using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// "This instance is producing that variant" (013 data-model.md §1.4, research MV-12).
/// </summary>
/// <remarks>
/// It exists only while a variant is being produced. Inserting it wins the production across
/// instances. A claim older than its lease belongs to an instance that died, and may be
/// taken over.
/// </remarks>
internal sealed class VariantClaim : ISiteOwned
{
    private VariantClaim()
    {
    }

    public VariantClaim(Guid siteId, Guid fileId, int contentVersion, int width, DateTimeOffset claimedAtUtc, Guid instanceId)
    {
        SiteId = siteId;
        FileId = fileId;
        ContentVersion = contentVersion;
        Width = width;
        ClaimedAtUtc = claimedAtUtc;
        InstanceId = instanceId;
    }

    public Guid SiteId { get; private set; }

    public Guid FileId { get; private set; }

    public int ContentVersion { get; private set; }

    public int Width { get; private set; }

    public DateTimeOffset ClaimedAtUtc { get; private set; }

    /// <summary>Random per process. Diagnostic only.</summary>
    public Guid InstanceId { get; private set; }
}
