using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// One file in one site's media library (003 data-model.md §1, 013 data-model.md §1.1, §3.1).
/// </summary>
/// <remarks>
/// <para>
/// A row means "complete, public and in the library". There is no status: an upload in
/// progress has no row yet (its bytes are written first), a deleted file has no row any
/// more, and files are public from the moment they are stored (003 clarifications).
/// </para>
/// <para>
/// <b>Mutable since 013.</b> A file can be filed in a folder, given alternative text, have
/// its image metadata recorded after the fact, and have its contents replaced. What never
/// changes is its identity: <see cref="Id"/>, <see cref="SiteId"/>, the display
/// <see cref="FileName"/>, <see cref="UploadedBy"/> and <see cref="UploadedAtUtc"/>. A
/// replacement is a new <see cref="ContentVersion"/> with its own storage key and address,
/// so the bytes behind any one address still never change (research MV-07, MV-08).
/// </para>
/// <para>
/// <see cref="Id"/> is random (v4, from the OS CSPRNG) because it is also the public
/// address. A time-ordered v7 id would be guessable and would leak upload times.
/// </para>
/// </remarks>
internal sealed partial class MediaFile : ISiteOwned, ITrashable
{
    public const int MaxContentTypeLength = 64;
    public const int MaxExtensionLength = 8;
    public const int MaxStorageKeyLength = 200;

    /// <summary>The longest alternative text, after cleaning (013 FR-008).</summary>
    public const int MaxAltTextLength = 300;

    /// <summary>The highest content version an address can carry (contracts/public-media.md §1).</summary>
    public const int MaxContentVersion = 99_999;

    private MediaFile()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Extension = string.Empty;
        StorageKey = string.Empty;
        SearchText = string.Empty;
        SortName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>The original name, cleaned for display (003 research M-17). Kept across replacements.</summary>
    public string FileName { get; private set; }

    /// <summary>One of the six allowed types, detected from the current version's content.</summary>
    public string ContentType { get; private set; }

    /// <summary>Lower-case, without the dot; part of the public address.</summary>
    public string Extension { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>When the first version was committed, which is when the file became public.</summary>
    public DateTimeOffset UploadedAtUtc { get; private set; }

    /// <summary>
    /// Who first uploaded it. Never changes, including on replacement: it is the uploader
    /// for every permission check (013 clarification).
    /// </summary>
    public Guid UploadedBy { get; private set; }

    /// <summary>Where the current version's bytes are, in the storage provider's key space.</summary>
    public string StorageKey { get; private set; }

    /// <summary>The folder it is filed in; null for the library's top level (013 MV-02).</summary>
    public Guid? FolderId { get; private set; }

    /// <summary>Alternative text, images only; null when none (013 MV-04).</summary>
    public string? AltText { get; private set; }

    /// <summary>Displayed width after EXIF orientation; null until recorded, or for documents.</summary>
    public int? Width { get; private set; }

    /// <summary>Displayed height; null together with <see cref="Width"/>.</summary>
    public int? Height { get; private set; }

    /// <summary>More than one frame or page (GIF, WebP or AVIF sequences).</summary>
    public bool IsAnimated { get; private set; }

    public MediaMetadataState MetadataState { get; private set; }

    /// <summary>1 for the upload; incremented by each replacement (013 MV-07).</summary>
    public int ContentVersion { get; private set; }

    public DateTimeOffset? ReplacedAtUtc { get; private set; }

    public Guid? ReplacedBy { get; private set; }

    public MediaVariantsState VariantsState { get; private set; }

    /// <summary>Normalised name and alternative text, for search (013 MV-03).</summary>
    public string SearchText { get; private set; }

    /// <summary>Normalised name, for sorting by name.</summary>
    public string SortName { get; private set; }

    /// <summary>Whether <see cref="SearchText"/> was built by <see cref="Domain.SearchText"/> or only lower-cased by the migration.</summary>
    public SearchTextState SearchTextState { get; private set; }

    /// <summary>
    /// PostgreSQL's <c>xmin</c>, the concurrency token for replace, move, alternative text and
    /// delete (013 data-model.md §1.1): a change made on a stale row fails as <c>media.conflict</c>.
    /// </summary>
    public uint Version { get; private set; }

    public static MediaFile Create(
        Guid id,
        Guid siteId,
        string fileName,
        MediaFileType type,
        string extension,
        long sizeBytes,
        DateTimeOffset uploadedAtUtc,
        Guid uploadedBy,
        string storageKey,
        Guid? folderId = null,
        ImageMetadata? metadata = null,
        MediaMetadataState? metadataState = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (fileName.Length > MediaFileName.MaxLength)
        {
            throw new ArgumentException($"A file name may be at most {MediaFileName.MaxLength} characters.", nameof(fileName));
        }

        CheckContent(type, extension, sizeBytes, metadata);

        var file = new MediaFile
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
            FolderId = folderId,
            ContentVersion = 1,
            VariantsState = MediaVariantsState.Available,
        };

        file.ApplyMetadata(type, metadata, metadataState);
        file.RefreshSearchText();
        return file;
    }

    /// <summary>The detected type, as a <see cref="MediaFileType"/>.</summary>
    public MediaFileType Type =>
        MediaFileType.FromMime(ContentType)
        ?? throw new InvalidOperationException($"Stored content type '{ContentType}' is not an allowed type.");

    /// <summary>Files it into <paramref name="folderId"/>, or the top level. The address does not change (FR-003).</summary>
    public void MoveTo(Guid? folderId) => FolderId = folderId;

    /// <summary>When the file was moved to the trash; null while it is live (021 TR-02).</summary>
    public DateTimeOffset? TrashedAtUtc { get; private set; }

    public Guid? TrashedBy { get; private set; }

    /// <summary>The folder it was in when trashed; null for the top level. No foreign key (TR-05).</summary>
    public Guid? TrashedFolderId { get; private set; }

    public bool IsTrashed => TrashedAtUtc is not null;

    /// <summary>
    /// Moves the file to the trash, keeping its bytes and variants (021 FR-003), and detaches
    /// it from its folder so an otherwise empty folder can be deleted (TR-05).
    /// </summary>
    public void MoveToTrash(Guid actor, DateTimeOffset now)
    {
        TrashedAtUtc = now;
        TrashedBy = actor;
        TrashedFolderId = FolderId;
        FolderId = null;
    }

    /// <summary>Takes the file out of the trash into <paramref name="folderId"/>, or the top level (021 FR-016).</summary>
    public void Restore(Guid? folderId)
    {
        TrashedAtUtc = null;
        TrashedBy = null;
        TrashedFolderId = null;
        FolderId = folderId;
    }

    /// <summary>Sets or clears the alternative text (013 FR-008).</summary>
    /// <remarks>
    /// Trimmed, with control and format characters removed; empty means none. Documents
    /// refuse any text; clearing is always allowed.
    /// </remarks>
    public Result SetAltText(string? text)
    {
        var cleaned = CleanAltText(text);

        if (cleaned is not null && !Type.IsImage)
        {
            return Result.Failure(MediaErrors.AltTextNotAllowed);
        }

        if (cleaned is { Length: > MaxAltTextLength })
        {
            return Result.Failure(MediaErrors.InvalidAltText);
        }

        AltText = cleaned;
        RefreshSearchText();
        return Result.Success();
    }

    /// <summary>
    /// Records an image's dimensions found after the fact (the upgrade backfill, MV-10).
    /// </summary>
    /// <returns>True when pages showing the file must re-render (it was pending before).</returns>
    public bool RecordImageMetadata(ImageMetadata metadata)
    {
        if (!Type.IsImage)
        {
            throw new InvalidOperationException("Only images have image metadata.");
        }

        var wasPending = MetadataState == MediaMetadataState.Pending;
        ApplyMetadata(Type, metadata);
        return wasPending;
    }

    /// <summary>Marks an image whose header was over the pixel limit or unreadable (backfill only).</summary>
    public void MarkMetadata(MediaMetadataState state)
    {
        if (state is not (MediaMetadataState.OverLimit or MediaMetadataState.Unreadable))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Only over_limit and unreadable are marked by hand.");
        }

        if (!Type.IsImage)
        {
            throw new InvalidOperationException("Only images have image metadata.");
        }

        MetadataState = state;
        Width = null;
        Height = null;
    }

    /// <summary>
    /// Replaces the contents with a new version (013 FR-017, research MV-07).
    /// </summary>
    /// <remarks>
    /// Keeps the id, display name, folder, alternative text, uploader and upload time.
    /// The replacement must be of the same kind; everything describing the content moves to
    /// the new version, and variants start over.
    /// </remarks>
    public Result Replace(
        MediaFileType type,
        string extension,
        long sizeBytes,
        string storageKey,
        ImageMetadata? metadata,
        Guid replacedBy,
        DateTimeOffset replacedAtUtc,
        MediaMetadataState? metadataState = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (type.IsImage != Type.IsImage)
        {
            return Result.Failure(MediaErrors.KindMismatch);
        }

        if (ContentVersion >= MaxContentVersion)
        {
            throw new InvalidOperationException($"A file cannot be replaced more than {MaxContentVersion - 1} times.");
        }

        CheckContent(type, extension, sizeBytes, metadata);

        ContentVersion++;
        ContentType = type.Mime;
        Extension = extension;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        ReplacedBy = replacedBy;
        ReplacedAtUtc = replacedAtUtc;
        VariantsState = MediaVariantsState.Available;
        ApplyMetadata(type, metadata, metadataState);
        return Result.Success();
    }

    /// <summary>
    /// Stops offering variants of <paramref name="contentVersion"/> after one could not be
    /// produced (MV-12).
    /// </summary>
    /// <returns>False when the file has moved on to another version, or was already marked.</returns>
    public bool MarkVariantsUnavailable(int contentVersion)
    {
        if (contentVersion != ContentVersion || VariantsState == MediaVariantsState.Unavailable)
        {
            return false;
        }

        VariantsState = MediaVariantsState.Unavailable;
        return true;
    }

    /// <summary>Rebuilds <see cref="SearchText"/> and <see cref="SortName"/> with the full normalisation.</summary>
    public void RefreshSearchText()
    {
        SearchText = Shared.Kernel.Text.SearchText.Combine(FileName, AltText);
        SortName = Truncate(Shared.Kernel.Text.SearchText.Normalise(FileName), MediaFileName.MaxLength);
        SearchTextState = SearchTextState.Normalised;
    }

    /// <summary>Cleans alternative text the way file names are cleaned: no control or format characters, trimmed.</summary>
    internal static string? CleanAltText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                builder.Append(rune.ToString());
            }
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <remarks>
    /// Without dimensions an image is pending (its header not read yet), unless the caller
    /// already knows it is unreadable: such a file is kept and served as uploaded, with no
    /// variants (research MV-10, revised).
    /// </remarks>
    private void ApplyMetadata(MediaFileType type, ImageMetadata? metadata, MediaMetadataState? state = null)
    {
        Width = null;
        Height = null;
        IsAnimated = false;

        if (!type.IsImage)
        {
            MetadataState = MediaMetadataState.NotApplicable;
            return;
        }

        if (metadata is { } m)
        {
            MetadataState = MediaMetadataState.Recorded;
            Width = m.Width;
            Height = m.Height;
            IsAnimated = m.IsAnimated;
            return;
        }

        MetadataState = state is MediaMetadataState.Unreadable ? MediaMetadataState.Unreadable : MediaMetadataState.Pending;
    }

    private static void CheckContent(MediaFileType type, string extension, long sizeBytes, ImageMetadata? metadata)
    {
        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "An empty file is never recorded (FR-008).");
        }

        if (!ExtensionPattern().IsMatch(extension))
        {
            throw new ArgumentException("The extension must be 1–8 lower-case letters or digits.", nameof(extension));
        }

        if (metadata is not null && !type.IsImage)
        {
            throw new ArgumentException("Only images have image metadata.", nameof(metadata));
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    [GeneratedRegex("^[a-z0-9]{1,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();
}

/// <summary>An image's displayed size and whether it moves (013 MV-10).</summary>
internal readonly record struct ImageMetadata
{
    public ImageMetadata(int width, int height, bool isAnimated)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        IsAnimated = isAnimated;
    }

    public int Width { get; }

    public int Height { get; }

    public bool IsAnimated { get; }

    /// <summary>Width × height, as the pixel limit counts it.</summary>
    public long Pixels => (long)Width * Height;
}

/// <summary>Whether an image's dimensions are known (013 data-model.md §1.1). Stored as smallint.</summary>
internal enum MediaMetadataState : short
{
    Pending = 0,
    Recorded = 1,
    NotApplicable = 2,
    OverLimit = 3,
    Unreadable = 4,
}

/// <summary>Whether variants of the current content version may be produced (MV-12). Stored as smallint.</summary>
internal enum MediaVariantsState : short
{
    Available = 0,
    Unavailable = 1,
}

/// <summary>How <see cref="MediaFile.SearchText"/> was built (013 data-model.md §1.1). Stored as smallint.</summary>
internal enum SearchTextState : short
{
    /// <summary>Lower-cased by the migration only; accents not yet folded.</summary>
    Provisional = 0,

    Normalised = 1,
}
