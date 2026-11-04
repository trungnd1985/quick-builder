namespace QuickBuilder.Media.Domain;

/// <summary>A media file restored from its own site's archive (020 research ST-12).</summary>
internal sealed partial class MediaFile
{
    /// <summary>Takes the content version the archive's bytes were stored under, and who replaced it last.</summary>
    public void RestoreContentVersion(int contentVersion, DateTimeOffset? replacedAtUtc, Guid? replacedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentVersion);
        ContentVersion = contentVersion;
        StorageKey = Infrastructure.MediaStorageKeys.For(SiteId, Id, contentVersion).Value;
        ReplacedAtUtc = replacedAtUtc;
        ReplacedBy = replacedBy;
    }
}
