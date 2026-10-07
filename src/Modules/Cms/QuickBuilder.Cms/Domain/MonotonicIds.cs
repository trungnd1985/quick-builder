namespace QuickBuilder.Cms.Domain;

/// <summary>
/// UUID v7 ids that increase strictly within this process, even within one millisecond.
/// </summary>
/// <remarks>
/// History entries are listed newest first by id (015 FR-031), and one operation writes several
/// at the same moment: a cancelled schedule, then a publish. <see cref="Guid.CreateVersion7()"/>
/// fills the bits after the millisecond at random, so ids of one millisecond come out in any
/// order. Here, an id in the same millisecond as the last one adds one to it instead, which keeps
/// the version, variant and time bits and orders the same way in .NET and in PostgreSQL.
/// </remarks>
internal static class MonotonicIds
{
    private static readonly Lock Gate = new();
    private static Guid _last;
    private static long _lastMillisecond;

    public static Guid Next(DateTimeOffset at)
    {
        var millisecond = at.ToUnixTimeMilliseconds();
        lock (Gate)
        {
            if (millisecond > _lastMillisecond || _last == Guid.Empty)
            {
                _last = Guid.CreateVersion7(at);
                _lastMillisecond = millisecond;
                return _last;
            }

            _last = Increment(_last);
            return _last;
        }
    }

    /// <summary>The id one greater, counting in its random bits (the last 8 bytes, then bytes 6-7's low 12 bits).</summary>
    private static Guid Increment(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);

        // Bytes 9…15 hold random bits only; byte 8 keeps its two variant bits.
        for (var i = 15; i >= 8; i--)
        {
            if (i == 8)
            {
                var low = (bytes[8] & 0x3F) + 1;
                bytes[8] = (byte)((bytes[8] & 0xC0) | (low & 0x3F));
                if (low <= 0x3F)
                {
                    break;
                }

                // Carry into rand_a, the low 12 bits of bytes 6-7, keeping the version nibble.
                var randA = (((bytes[6] & 0x0F) << 8) | bytes[7]) + 1;
                bytes[6] = (byte)((bytes[6] & 0xF0) | ((randA >> 8) & 0x0F));
                bytes[7] = (byte)randA;
                break;
            }

            if (++bytes[i] != 0)
            {
                break;
            }
        }

        return new Guid(bytes, bigEndian: true);
    }
}
