using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// Known devices: browsers exempt from the per-account wait (spec FR-015a; research R-07).
/// </summary>
/// <remarks>
/// The <c>qb_device</c> cookie holds up to <see cref="MaxTokensPerBrowser"/> opaque tokens,
/// one per account signed in from that browser; the database holds their SHA-256. Every
/// method adds to the caller's unit of work and never saves, so device changes commit with
/// the sign-in or revocation that caused them.
/// </remarks>
internal sealed class KnownDeviceService(
    IdentityDbContext database,
    IOptions<IdentityOptions> options,
    SecurityEventRecorder events,
    TimeProvider clock)
{
    public const int MaxTokensPerBrowser = 5;

    private const char Separator = '.';

    private readonly IdentityOptions.SignInSettings _settings = options.Value.SignIn;

    /// <summary>The valid known device this browser holds for <paramref name="userId"/>, if any.</summary>
    public async Task<KnownDevice?> FindValidAsync(HttpContext context, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hashes = ReadTokens(context).Select(Tokens.Hash).ToList();
        if (hashes.Count == 0)
        {
            return null;
        }

        var candidates = await database.KnownDevices
            .Where(d => d.UserId == userId && d.RevokedAtUtc == null && hashes.Contains(d.TokenHash))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        return candidates.Find(d => d.IsValid(now, _settings.KnownDeviceLifetime));
    }

    /// <summary>
    /// After a successful sign-in: refresh the device the browser presented, or remember a
    /// new one, and rewrite the cookie keeping the most recently used tokens.
    /// </summary>
    public void Remember(HttpContext context, Guid userId, KnownDevice? presented)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = clock.GetUtcNow();
        var tokens = ReadTokens(context);

        if (presented is not null)
        {
            presented.RecordSuccess(now);

            // Move the presented token to the front so it survives trimming.
            var presentedToken = tokens.Find(t => Tokens.Hash(t).AsSpan().SequenceEqual(presented.TokenHash));
            if (presentedToken is not null)
            {
                tokens.Remove(presentedToken);
                tokens.Insert(0, presentedToken);
            }
        }
        else
        {
            var token = Tokens.NewDeviceToken();
            database.KnownDevices.Add(KnownDevice.Remember(userId, Tokens.Hash(token), now));
            tokens.Insert(0, token);
        }

        context.Response.Cookies.Append(
            IdentityCookies.Device,
            string.Join(Separator, tokens.Take(MaxTokensPerBrowser)),
            IdentityCookies.Options(context, _settings.KnownDeviceLifetime));
    }

    /// <summary>A failed attempt from a known device; revokes it at the threshold.</summary>
    public void RecordFailure(KnownDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.RecordFailure(_settings.KnownDeviceFailureThreshold, clock.GetUtcNow()))
        {
            events.Record(
                SecurityEventTypes.KnownDeviceRevoked,
                SecurityEventOutcomes.Succeeded,
                actorUserId: null,
                subjectUserId: device.UserId,
                new ReasonDetails("failures"));
        }
    }

    /// <summary>
    /// Revokes every device of a user: deactivation, email change, set-password link use and
    /// administrator "sign out everywhere" (research R-07). Not the owner's own password
    /// change or "sign out my other sessions".
    /// </summary>
    public async Task RevokeAllAsync(Guid userId, Guid? actorUserId, string reason, CancellationToken cancellationToken)
    {
        var devices = await database.KnownDevices
            .Where(d => d.UserId == userId && d.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (devices.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var device in devices)
        {
            device.Revoke(now);
        }

        events.Record(
            SecurityEventTypes.KnownDeviceRevoked,
            SecurityEventOutcomes.Succeeded,
            actorUserId,
            userId,
            new ReasonDetails(reason));
    }

    private static List<string> ReadTokens(HttpContext context) =>
        context.Request.Cookies.TryGetValue(IdentityCookies.Device, out var raw) && !string.IsNullOrEmpty(raw)
            ? [.. raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Take(MaxTokensPerBrowser)]
            : [];
}
