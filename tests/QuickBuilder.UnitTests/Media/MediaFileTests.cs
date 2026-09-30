using QuickBuilder.Media.Domain;

namespace QuickBuilder.UnitTests.Media;

/// <summary>The rules of a library file (013 data-model.md §3.1).</summary>
public sealed class MediaFileTests
{
    private static readonly Guid Uploader = Guid.NewGuid();
    private static readonly DateTimeOffset UploadedAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static MediaFile Image(ImageMetadata? metadata = null, string name = "Été photo.jpg") =>
        MediaFile.Create(
            Guid.NewGuid(), Guid.NewGuid(), name, MediaFileType.Jpeg, "jpg", 1_000,
            UploadedAt, Uploader, "site/media/file", metadata: metadata);

    private static MediaFile Pdf() =>
        MediaFile.Create(
            Guid.NewGuid(), Guid.NewGuid(), "brochure.pdf", MediaFileType.Pdf, "pdf", 1_000,
            UploadedAt, Uploader, "site/media/file");

    [Fact]
    public void A_new_image_without_metadata_is_pending_and_version_one()
    {
        var file = Image();

        Assert.Equal(1, file.ContentVersion);
        Assert.Equal(MediaMetadataState.Pending, file.MetadataState);
        Assert.Null(file.Width);
        Assert.Equal(MediaVariantsState.Available, file.VariantsState);
        Assert.Equal(SearchTextState.Normalised, file.SearchTextState);
        Assert.Equal("ete photo.jpg", file.SearchText);
        Assert.Equal("ete photo.jpg", file.SortName);
    }

    [Fact]
    public void A_new_image_with_metadata_is_recorded()
    {
        var file = Image(new ImageMetadata(4000, 6000, isAnimated: false));

        Assert.Equal(MediaMetadataState.Recorded, file.MetadataState);
        Assert.Equal(4000, file.Width);
        Assert.Equal(6000, file.Height);
    }

    [Fact]
    public void A_document_has_no_image_metadata() =>
        Assert.Equal(MediaMetadataState.NotApplicable, Pdf().MetadataState);

    [Fact]
    public void Documents_refuse_metadata_at_creation() =>
        Assert.Throws<ArgumentException>(() => MediaFile.Create(
            Guid.NewGuid(), Guid.NewGuid(), "a.pdf", MediaFileType.Pdf, "pdf", 10,
            UploadedAt, Uploader, "k", metadata: new ImageMetadata(10, 10, false)));

    [Fact]
    public void Moving_keeps_everything_but_the_folder()
    {
        var file = Image();
        var folder = Guid.NewGuid();

        file.MoveTo(folder);
        Assert.Equal(folder, file.FolderId);
        Assert.Equal(1, file.ContentVersion);

        file.MoveTo(null);
        Assert.Null(file.FolderId);
    }

    [Fact]
    public void Alternative_text_is_cleaned_and_searchable()
    {
        var file = Image();

        Assert.True(file.SetAltText("  Anna ‮at her desk\u0007 ").IsSuccess);
        Assert.Equal("Anna at her desk", file.AltText);
        Assert.Contains("anna at her desk", file.SearchText, StringComparison.Ordinal);

        Assert.True(file.SetAltText("   ").IsSuccess);
        Assert.Null(file.AltText);
    }

    [Fact]
    public void Alternative_text_is_limited_to_300_characters()
    {
        var file = Image();

        Assert.True(file.SetAltText(new string('a', 300)).IsSuccess);

        var refused = file.SetAltText(new string('a', 301));
        Assert.Equal("media.invalid_alt_text", refused.Error!.Code);
        Assert.Equal(300, file.AltText!.Length);
    }

    [Fact]
    public void Documents_refuse_alternative_text_but_may_clear_it()
    {
        var file = Pdf();

        Assert.Equal("media.alt_text_not_allowed", file.SetAltText("A brochure").Error!.Code);
        Assert.True(file.SetAltText(null).IsSuccess);
    }

    [Fact]
    public void Recording_metadata_after_the_fact_reports_whether_pages_must_refresh()
    {
        var file = Image();

        Assert.True(file.RecordImageMetadata(new ImageMetadata(800, 600, false)));
        Assert.Equal(MediaMetadataState.Recorded, file.MetadataState);
        Assert.False(file.RecordImageMetadata(new ImageMetadata(800, 600, false)));
    }

    [Fact]
    public void Over_limit_and_unreadable_are_marked_without_dimensions()
    {
        var file = Image();

        file.MarkMetadata(MediaMetadataState.OverLimit);
        Assert.Equal(MediaMetadataState.OverLimit, file.MetadataState);
        Assert.Null(file.Width);
        Assert.Throws<ArgumentOutOfRangeException>(() => file.MarkMetadata(MediaMetadataState.Recorded));
    }

    [Fact]
    public void Replacing_keeps_identity_and_moves_the_content_on()
    {
        var file = Image(new ImageMetadata(100, 100, false));
        var folder = Guid.NewGuid();
        file.MoveTo(folder);
        file.SetAltText("Logo");
        file.MarkVariantsUnavailable(1);
        var replacer = Guid.NewGuid();
        var at = UploadedAt.AddDays(3);
        var (id, name, uploadedAt) = (file.Id, file.FileName, file.UploadedAtUtc);

        var result = file.Replace(MediaFileType.Png, "png", 2_000, "site/media/file-v2", new ImageMetadata(200, 50, false), replacer, at);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, file.ContentVersion);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("png", file.Extension);
        Assert.Equal(2_000, file.SizeBytes);
        Assert.Equal("site/media/file-v2", file.StorageKey);
        Assert.Equal((200, 50), (file.Width, file.Height));
        Assert.Equal(MediaVariantsState.Available, file.VariantsState);
        Assert.Equal(replacer, file.ReplacedBy);
        Assert.Equal(at, file.ReplacedAtUtc);

        // Identity, filing and description stay; the uploader never changes (clarification).
        Assert.Equal((id, name, uploadedAt), (file.Id, file.FileName, file.UploadedAtUtc));
        Assert.Equal(Uploader, file.UploadedBy);
        Assert.Equal(folder, file.FolderId);
        Assert.Equal("Logo", file.AltText);
    }

    [Fact]
    public void Replacing_with_another_kind_is_refused_and_changes_nothing()
    {
        var file = Image();

        var result = file.Replace(MediaFileType.Pdf, "pdf", 10, "k2", null, Guid.NewGuid(), UploadedAt);

        Assert.Equal("media.kind_mismatch", result.Error!.Code);
        Assert.Equal(1, file.ContentVersion);
        Assert.Equal("image/jpeg", file.ContentType);
    }

    [Fact]
    public void Variants_are_marked_unavailable_only_for_the_current_version()
    {
        var file = Image();

        Assert.False(file.MarkVariantsUnavailable(2));
        Assert.True(file.MarkVariantsUnavailable(1));
        Assert.False(file.MarkVariantsUnavailable(1));
        Assert.Equal(MediaVariantsState.Unavailable, file.VariantsState);
    }
}
