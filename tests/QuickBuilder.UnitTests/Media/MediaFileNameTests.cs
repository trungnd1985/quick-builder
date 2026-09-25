using QuickBuilder.Media.Domain;

namespace QuickBuilder.UnitTests.Media;

/// <summary>File names are cleaned for display and never used for storage (003 research M-17).</summary>
public sealed class MediaFileNameTests
{
    [Theory]
    [InlineData("team photo.jpg", "team photo.jpg")]
    [InlineData("team%20photo.jpg", "team photo.jpg")]
    [InlineData("C:\\Users\\me\\Pictures\\logo.png", "logo.png")]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("  spaced.pdf  ", "spaced.pdf")]
    [InlineData("..hidden.gif.", "hidden.gif")]
    [InlineData("tab\there.png", "tabhere.png")]
    [InlineData("Ảnh đẹp.webp", "Ảnh đẹp.webp")]
    [InlineData("写真.avif", "写真.avif")]
    public void Cleans_names(string raw, string expected) =>
        Assert.Equal(expected, MediaFileName.Clean(raw));

    [Fact]
    public void Strips_the_right_to_left_override_that_disguises_extensions()
    {
        // "invoice\u202Egpj.exe" displays as "invoiceexe.jpg".
        Assert.Equal("invoicegpj.exe", MediaFileName.Clean("invoice\u202Egpj.exe"));
    }

    [Fact]
    public void Keeps_combining_marks_as_given()
    {
        // Not normalised: invariant globalization makes Normalize a no-op (research M-17).
        var decomposed = "Cafe\u0301.png";

        Assert.Equal(decomposed, MediaFileName.Clean(decomposed));
    }

    [Fact]
    public void Replaces_a_lone_surrogate_rather_than_failing() =>
        Assert.Equal("a\ufffd.png", MediaFileName.Clean("a\uD800.png"));

    [Fact]
    public void Shortens_long_names_keeping_the_extension()
    {
        var cleaned = MediaFileName.Clean(new string('x', 300) + ".jpeg");

        Assert.NotNull(cleaned);
        Assert.Equal(MediaFileName.MaxLength, cleaned.Length);
        Assert.EndsWith(".jpeg", cleaned, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(" . . ")]
    [InlineData("folder/")]
    [InlineData("\u0000\u0001")]
    public void Returns_null_when_nothing_usable_remains(string? raw) =>
        Assert.Null(MediaFileName.Clean(raw));

    [Theory]
    [InlineData("photo.JPG", "jpg")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("noextension", "")]
    [InlineData("trailingdot.", "")]
    [InlineData(".png", "")]
    public void Extracts_the_lower_case_extension(string cleaned, string expected) =>
        Assert.Equal(expected, MediaFileName.Extension(cleaned));
}
