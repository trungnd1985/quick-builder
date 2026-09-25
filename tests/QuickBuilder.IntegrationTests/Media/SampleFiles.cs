using System.Buffers.Binary;
using System.Text;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Minimal, real files of each allowed type, and ways to bend them (003 research M-20).
/// </summary>
/// <remarks>
/// Generated in code rather than committed as binaries, so every case is readable in review.
/// </remarks>
internal static class SampleFiles
{
    /// <summary>A 1×1 JPEG.</summary>
    public static byte[] Jpeg { get; } = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////" +
        "wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=");

    /// <summary>A 1×1 PNG.</summary>
    public static byte[] Png { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>A 1×1 GIF.</summary>
    public static byte[] Gif { get; } = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>A 1×1 lossless WebP.</summary>
    public static byte[] WebP { get; } = Convert.FromBase64String(
        "UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==");

    /// <summary>The start of an AVIF: an <c>ftyp</c> box naming <c>avif</c>, then an empty <c>meta</c>.</summary>
    public static byte[] Avif { get; } = Ftyp("avif", "mif1", "miaf").Concat(new byte[] { 0, 0, 0, 8, (byte)'m', (byte)'e', (byte)'t', (byte)'a' }).ToArray();

    /// <summary>A one-page PDF.</summary>
    public static byte[] Pdf { get; } = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 10 10]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>Every allowed type: (bytes, extension, canonical type).</summary>
    public static IReadOnlyList<(byte[] Bytes, string Extension, string ContentType)> All { get; } =
    [
        (Jpeg, "jpg", "image/jpeg"),
        (Png, "png", "image/png"),
        (Gif, "gif", "image/gif"),
        (WebP, "webp", "image/webp"),
        (Avif, "avif", "image/avif"),
        (Pdf, "pdf", "application/pdf"),
    ];

    /// <summary>An ISO-BMFF <c>ftyp</c> box with the given major and compatible brands.</summary>
    public static byte[] Ftyp(string major, params string[] compatible)
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

    /// <summary><paramref name="head"/> padded with zeros to exactly <paramref name="size"/> bytes.</summary>
    public static byte[] OfSize(byte[] head, long size)
    {
        var bytes = new byte[size];
        head.AsSpan(0, (int)Math.Min(head.Length, size)).CopyTo(bytes);
        return bytes;
    }

    /// <summary><paramref name="head"/> followed by <paramref name="tail"/>.</summary>
    public static byte[] Then(this byte[] head, string tail) => [.. head, .. Encoding.UTF8.GetBytes(tail)];
}
