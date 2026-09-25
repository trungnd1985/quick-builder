using System.Text.RegularExpressions;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// One file in one site's media library (003 data-model.md §1).
/// </summary>
/// <remarks>
/// A row means "complete, public and in the library". There is no status: an upload in
/// progress has no row yet (its bytes are written first), a deleted file has no row any
/// more, and files are public from the moment they are stored (spec clarifications). The
/// contents never change (FR-005), so nothing here is mutable after creation.
/// <para>
/// <see cref="Id"/> is random (v4, from the OS CSPRNG) because it is also the public
/// address. A time-ordered v7 id would be guessable and would leak upload times.
/// </para>
/// </remarks>
internal sealed partial class MediaFile : ISiteOwned
{
    public const int MaxContentTypeLength = 64;
    public const int MaxExtensionLength = 8;
    public const int MaxStorageKeyLength = 200;

    private MediaFile()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Extension = string.Empty;
        StorageKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>The original name, cleaned for display (research M-17).</summary>
    public string FileName { get; private set; }

    /// <summary>One of the six allowed types, detected from content.</summary>
    public string ContentType { get; private set; }

    /// <summary>Lower-case, without the dot; part of the public address.</summary>
    public string Extension { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>When the row was committed, which is when the file became public.</summary>
    public DateTimeOffset UploadedAtUtc { get; private set; }

    /// <summary>The uploading user's id; names come from Identity's <c>IUserDirectory</c>.</summary>
    public Guid UploadedBy { get; private set; }

    /// <summary>Where the bytes are, in the storage provider's key space.</summary>
    public string StorageKey { get; private set; }

    public static MediaFile Create(
        Guid id,
        Guid siteId,
        string fileName,
        MediaFileType type,
        string extension,
        long sizeBytes,
        DateTimeOffset uploadedAtUtc,
        Guid uploadedBy,
        string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (fileName.Length > MediaFileName.MaxLength)
        {
            throw new ArgumentException($"A file name may be at most {MediaFileName.MaxLength} characters.", nameof(fileName));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "An empty file is never recorded (FR-008).");
        }

        if (!ExtensionPattern().IsMatch(extension))
        {
            throw new ArgumentException("The extension must be 1–8 lower-case letters or digits.", nameof(extension));
        }

        return new MediaFile
        {
            Id = id,
            SiteId = siteId,
            FileName = fileName,
            ContentType = type.Mime,
            Extension = extension,
            SizeBytes = sizeBytes,
            UploadedAtUtc = uploadedAtUtc,
            UploadedBy = uploadedBy,
            StorageKey = storageKey,
        };
    }

    /// <summary>The detected type, as a <see cref="MediaFileType"/>.</summary>
    public MediaFileType Type =>
        MediaFileType.FromMime(ContentType)
        ?? throw new InvalidOperationException($"Stored content type '{ContentType}' is not an allowed type.");

    [GeneratedRegex("^[a-z0-9]{1,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();
}
