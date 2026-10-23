using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.Network;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>Whether a request is over a limit, and for how long.</summary>
internal sealed record RateDecision(bool Limited, TimeSpan RetryAfter)
{
    public static RateDecision Allowed { get; } = new(false, TimeSpan.Zero);
}

/// <summary>
/// The per-site, per-address rate limit of submit requests (018 FR-017; research FS-13): every
/// request counts in its minute and hour windows, in the database, so every instance sees the
/// same counts.
/// </summary>
/// <remarks>
/// The client is keyed by a SHA-256 of its address, grouped as sign-in throttling groups it
/// (IPv6 by /64). That key is written to <c>rate_windows</c> only and swept within the hour
/// (FR-021).
/// </remarks>
internal sealed class RateWindows(FormsDbContext database, IOptions<FormsOptions> options, TimeProvider clock)
{
    public async Task<RateDecision> CountAsync(Guid siteId, IPAddress? address, CancellationToken cancellationToken)
    {
        var limits = options.Value.RateLimit;
        var now = clock.GetUtcNow();
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var hour = minute.AddMinutes(-now.Minute);

        var (perMinute, perHour) = await database.CountRequestAsync(siteId, Key(address), minute, hour, cancellationToken).ConfigureAwait(false);

        if (perHour > limits.PerHour)
        {
            return new RateDecision(true, hour.AddHours(1) - now);
        }

        return perMinute > limits.PerMinute ? new RateDecision(true, minute.AddMinutes(1) - now) : RateDecision.Allowed;
    }

    /// <summary>The stored key for an address.</summary>
    internal static byte[] Key(IPAddress? address) => SHA256.HashData(Encoding.UTF8.GetBytes(ClientAddress.Group(address)));
}
