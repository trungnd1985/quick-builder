using System.Buffers.Binary;
using QuickBuilder.Media.Domain;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Recognises the six allowed types from a file's first bytes (003 FR-006, research M-07).
/// </summary>
/// <remarks>
/// Six fixed signatures, written by hand: a library would add a dependency to recognise
/// formats that must be refused anyway (Principle XI). Nothing is decoded or parsed beyond
/// the signature — safety comes from how files are served (M-11), not from proving a file is
/// a well-formed image. A file that starts with an allowed signature and also contains HTML
/// or script is accepted as its leading type; the pinned type and <c>nosniff</c> ensure it
/// is never interpreted as anything else.
/// </remarks>
internal static class FileTypeDetector
{
    /// <summary>How many leading bytes detection needs. The upload handler reads this much first.</summary>
    public const int HeadLength = 4096;

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> Gif87a => "GIF87a"u8;

    private static ReadOnlySpan<byte> Gif89a => "GIF89a"u8;

    private static ReadOnlySpan<byte> Riff => "RIFF"u8;

    private static ReadOnlySpan<byte> WebP => "WEBP"u8;

    private static ReadOnlySpan<byte> Ftyp => "ftyp"u8;

    private static ReadOnlySpan<byte> Pdf => "%PDF-"u8;

    /// <summary>The type <paramref name="head"/> begins with, or null for anything else.</summary>
    public static MediaFileType? Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(JpegSignature))
        {
            return MediaFileType.Jpeg;
        }

        if (head.StartsWith(PngSignature))
        {
            return MediaFileType.Png;
        }

        if (head.StartsWith(Gif87a) || head.StartsWith(Gif89a))
        {
            return MediaFileType.Gif;
        }

        if (head.Length >= 12 && head.StartsWith(Riff) && head.Slice(8, 4).SequenceEqual(WebP))
        {
            return MediaFileType.WebP;
        }

        if (IsAvif(head))
        {
            return MediaFileType.Avif;
        }

        if (head.StartsWith(Pdf))
        {
            return MediaFileType.Pdf;
        }

        return null;
    }

    /// <summary>
    /// An ISO-BMFF <c>ftyp</c> box at offset 4 whose major brand, or one of whose compatible
    /// brands, is <c>avif</c> or <c>avis</c>. Only brands inside the declared box size (and
    /// inside <paramref name="head"/>) count.
    /// </summary>
    private static bool IsAvif(ReadOnlySpan<byte> head)
    {
        // size(4) 'ftyp'(4) major(4) minor-version(4) compatible(4 each)...
        if (head.Length < 16 || !head.Slice(4, 4).SequenceEqual(Ftyp))
        {
            return false;
        }

        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (boxSize < 16)
        {
            return false;
        }

        var end = (int)Math.Min(boxSize, (uint)head.Length);

        if (IsAvifBrand(head.Slice(8, 4)))
        {
            return true;
        }

        for (var offset = 16; offset + 4 <= end; offset += 4)
        {
            if (IsAvifBrand(head.Slice(offset, 4)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAvifBrand(ReadOnlySpan<byte> brand) =>
        brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8);
}
