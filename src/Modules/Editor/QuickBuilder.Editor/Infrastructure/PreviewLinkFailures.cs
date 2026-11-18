using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Kernel.Network;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Slows down guessing at preview links (022 research PV-18, spec FR-013): unknown links from
/// one address group are counted in minute and hour windows, in the database, so every instance
/// sees the same counts.
/// </summary>
/// <remarks>
/// A 256-bit secret cannot be found by guessing; this stops wasted load and noise. Only unknown
/// links count: an expired or revoked link is a real link someone was given. Past a limit, every
/// link request of the group, valid ones too, waits until the window ends. The address is stored
/// as a SHA-256 of its group (IPv6 by /64), as 018's rate windows do, and swept within the hour.
/// </remarks>
internal sealed class PreviewLinkFailures(EditorDbContext database, TimeProvider clock)
{
    public const int PerMinute = 20;

    public const int PerHour = 200;

    /// <summary>How long <paramref name="client"/> must wait before trying links again; null when it need not.</summary>
    public async Task<TimeSpan?> RetryAfterAsync(IPAddress? client, CancellationToken cancellationToken)
    {
        var (key, now, minute, hour) = Windows(client);
        var counts = await database.PreviewLinkFailures
            .AsNoTracking()
            .Where(w => w.ClientKey == key
                && ((w.WindowKind == PreviewLinkFailureWindow.Minute && w.WindowStartUtc == minute)
                    || (w.WindowKind == PreviewLinkFailureWindow.Hour && w.WindowStartUtc == hour)))
            .Select(w => new { w.WindowKind, w.Count })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (counts.Exists(c => c.WindowKind == PreviewLinkFailureWindow.Hour && c.Count >= PerHour))
        {
            return hour.AddHours(1) - now;
        }

        return counts.Exists(c => c.WindowKind == PreviewLinkFailureWindow.Minute && c.Count >= PerMinute)
            ? minute.AddMinutes(1) - now
            : null;
    }

    /// <summary>Counts one unknown link from <paramref name="client"/> in its current windows.</summary>
    /// <remarks>
    /// One atomic upsert through parameterised SQL (EF has no <c>ON CONFLICT</c>): installation
    /// data, not site content, so no site filter applies.
    /// </remarks>
    public async Task CountAsync(IPAddress? client, CancellationToken cancellationToken)
    {
        var (key, _, minute, hour) = Windows(client);
        await database.Database.ExecuteSqlAsync($"""
            INSERT INTO editor.preview_link_failures AS w (client_key, window_kind, window_start_utc, count)
            VALUES ({key}, {PreviewLinkFailureWindow.Minute}, {minute}, 1), ({key}, {PreviewLinkFailureWindow.Hour}, {hour}, 1)
            ON CONFLICT (client_key, window_kind, window_start_utc) DO UPDATE SET count = w.count + 1
            """, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The stored key for an address.</summary>
    internal static byte[] Key(IPAddress? client) => SHA256.HashData(Encoding.UTF8.GetBytes(ClientAddress.Group(client)));

    private (byte[] Key, DateTimeOffset Now, DateTimeOffset Minute, DateTimeOffset Hour) Windows(IPAddress? client)
    {
        var now = clock.GetUtcNow();
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        return (Key(client), now, minute, minute.AddMinutes(-now.Minute));
    }
}
