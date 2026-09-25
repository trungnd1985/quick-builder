using System.Buffers.Binary;
using System.Text;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Content-based type detection (003 FR-006, research M-07).</summary>
public sealed class FileTypeDetectorTests
{
    public static TheoryData<byte[], string> Signatures => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10], "image/jpeg" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D], "image/png" },
        { Ascii("GIF87a\x01\x00"), "image/gif" },
        { Ascii("GIF89a\x01\x00"), "image/gif" },
        { Ascii("RIFF\x1a\x00\x00\x00WEBPVP8L"), "image/webp" },
        { Ftyp("avif", "mif1"), "image/avif" },
        { Ftyp("avis", "msf1"), "image/avif" },
        { Ftyp("mif1", "miaf", "avif"), "image/avif" },
        { Ascii("%PDF-1.7\n"), "application/pdf" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Recognises_each_allowed_signature(byte[] head, string expected) =>
        Assert.Equal(expected, FileTypeDetector.Detect(head)?.Mime);

    public static TheoryData<byte[]> NearMisses => new()
    {
        Array.Empty<byte>(),
        new byte[] { 0xFF, 0xD8 },
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A },
        Ascii("GIF88a"),
        Ascii("GIF8"),
        Ascii("RIFF\x1a\x00\x00\x00WAVEfmt "),
        Ascii("RIFF\x1a\x00"),
        Ftyp("mif1", "miaf"),
        Ftyp("heic", "mif1", "heic"),
        Ascii("%PDF"),
        Ascii(" %PDF-1.4"),
        Ascii("<!DOCTYPE html><script>alert(1)</script>"),
        Ascii("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
        Ascii("MZ\x90\x00"),
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' },
        new byte[] { 0x50, 0x4B, 0x03, 0x04 },
    };

    [Theory]
    [MemberData(nameof(NearMisses))]
    public void Refuses_truncated_near_miss_and_foreign_headers(byte[] head) =>
        Assert.Null(FileTypeDetector.Detect(head));

    [Fact]
    public void An_avif_brand_past_the_declared_box_size_does_not_count()
    {
        // Box claims 16 bytes (major + minor only); "avif" sits just beyond it.
        var head = Ftyp("mif1", "avif");
        BinaryPrimitives.WriteUInt32BigEndian(head, 16);

        Assert.Null(FileTypeDetector.Detect(head));
    }

    [Fact]
    public void A_polyglot_that_leads_with_an_allowed_signature_is_that_type()
    {
        // Accepted as GIF; serving pins the type so it is never read as script (M-07, M-11).
        var polyglot = Ascii("GIF89a/*\x01\x00*/=1;alert(document.cookie)//<script>alert(1)</script>");

        Assert.Same(MediaFileType.Gif, FileTypeDetector.Detect(polyglot));
    }

    private static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var size = 16 + (4 * compatible.Length);
        var box = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)size);
        Encoding.ASCII.GetBytes("ftyp").CopyTo(box, 4);
        Encoding.ASCII.GetBytes(major).CopyTo(box, 8);
        for (var i = 0; i < compatible.Length; i++)
        {
            Encoding.ASCII.GetBytes(compatible[i]).CopyTo(box, 16 + (4 * i));
        }

        return box;
    }
}
