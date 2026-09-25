using QuickBuilder.Media.Domain;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Extension and declared type must agree with the detected type (003 FR-006, research M-08).</summary>
public sealed class MediaFileTypeTests
{
    [Fact]
    public void The_built_in_list_is_exactly_the_six_allowed_types() =>
        Assert.Equal(
            ["image/jpeg", "image/png", "image/gif", "image/webp", "image/avif", "application/pdf"],
            MediaFileType.All.Select(t => t.Mime));

    [Theory]
    [InlineData("jpg")]
    [InlineData("JPG")]
    [InlineData("jpeg")]
    [InlineData("Jpeg")]
    public void Extensions_compare_case_insensitively(string extension) =>
        Assert.True(MediaFileType.Jpeg.Accepts(extension, "image/jpeg"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/octet-stream")]
    [InlineData("APPLICATION/OCTET-STREAM")]
    [InlineData("image/png")]
    [InlineData("image/png; charset=binary")]
    public void An_undeclared_or_matching_type_is_accepted(string? declared) =>
        Assert.True(MediaFileType.Png.Accepts("png", declared));

    [Theory]
    [InlineData("gif", "image/png")]
    [InlineData("png", "image/gif")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("", "image/png")]
    [InlineData(null, "image/png")]
    [InlineData("png", "text/html")]
    [InlineData("svg", "image/svg+xml")]
    public void A_contradiction_or_missing_extension_is_refused(string? extension, string? declared) =>
        Assert.False(MediaFileType.Png.Accepts(extension, declared));

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("video/mp4")]
    [InlineData(null)]
    public void Types_outside_the_list_do_not_exist(string? mime) =>
        Assert.Null(MediaFileType.FromMime(mime));

    [Fact]
    public void Only_images_are_marked_as_images() =>
        Assert.Equal(
            [true, true, true, true, true, false],
            MediaFileType.All.Select(t => t.IsImage));
}
