using System.Buffers.Binary;
using System.Text;

namespace QuickBuilder.Media.Features.ListFiles;

/// <summary>How a listing is ordered (013 research MV-03).</summary>
internal enum ListingSort : byte
{
    Uploaded = 1,
    Name = 2,
    Size = 3,
}

/// <summary>
/// A position in one listing: the last row a page returned, under the sort and query that
/// produced it (013 research MV-03; 003 research M-16).
/// </summary>
/// <remarks>
/// Opaque to clients (base64url). It carries the sort, the direction and a fingerprint of
/// the query's filters, so a cursor is refused when reused with a different sort, folder,
/// search or kind, rather than silently skipping or repeating files.
/// </remarks>
internal sealed record ListingCursor(
    ListingSort Sort,
    bool Descending,
    uint QueryFingerprint,
    Guid Id,
    long NumberKey,
    string? TextKey)
{
    private const byte FormatVersion = 1;
    private const int HeaderLength = 1 + 1 + 1 + 4 + 16;

    public string Encode()
    {
        var text = TextKey is null ? [] : Encoding.UTF8.GetBytes(TextKey);
        var bytes = new byte[HeaderLength + 8 + text.Length];

        bytes[0] = FormatVersion;
        bytes[1] = (byte)Sort;
        bytes[2] = Descending ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(3), QueryFingerprint);
        Id.TryWriteBytes(bytes.AsSpan(7, 16));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(HeaderLength), NumberKey);
        text.CopyTo(bytes.AsSpan(HeaderLength + 8));

        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// The cursor, or null when <paramref name="value"/> is not one this server issued for
    /// this sort, direction and query.
    /// </summary>
    public static ListingCursor? TryDecode(string value, ListingSort sort, bool descending, uint queryFingerprint)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > 2_048)
        {
            return null;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

        var buffer = new byte[(base64.Length / 4 * 3) + 3];
        if (!Convert.TryFromBase64String(base64, buffer, out var length) || length < HeaderLength + 8)
        {
            return null;
        }

        var bytes = buffer.AsSpan(0, length);
        if (bytes[0] != FormatVersion
            || bytes[1] != (byte)sort
            || bytes[2] != (descending ? 1 : 0)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes[3..]) != queryFingerprint)
        {
            return null;
        }

        var id = new Guid(bytes.Slice(7, 16));
        var number = BinaryPrimitives.ReadInt64BigEndian(bytes[HeaderLength..]);

        string? text = null;
        if (sort == ListingSort.Name)
        {
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes[(HeaderLength + 8)..]);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
        else if (length != HeaderLength + 8)
        {
            return null;
        }

        if (sort == ListingSort.Uploaded
            && (number < DateTimeOffset.MinValue.UtcTicks || number > DateTimeOffset.MaxValue.UtcTicks))
        {
            return null;
        }

        return new ListingCursor(sort, descending, queryFingerprint, id, number, text);
    }

    /// <summary>
    /// A stable fingerprint of the filters a cursor belongs to (FNV-1a over their text).
    /// Not a security measure: it only stops a cursor being reused with another query.
    /// </summary>
    public static uint Fingerprint(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var hash = 2166136261u;
        foreach (var part in parts)
        {
            foreach (var b in Encoding.UTF8.GetBytes((part ?? "\u0000") + "\u001f"))
            {
                hash ^= b;
                hash *= 16777619u;
            }
        }

        return hash;
    }
}
