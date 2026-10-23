using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// Per site, form and day, how many submissions spam protection held back (018 FR-019;
/// data-model §1, <c>spam_counters</c>). Counts only, never values.
/// </summary>
internal sealed class SpamCounter : ISiteOwned
{
    /// <summary>How long counters are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public SpamCounter(Guid siteId, string formId, DateOnly day)
    {
        SiteId = siteId;
        FormId = formId;
        Day = day;
    }

    public Guid SiteId { get; private set; }

    public string FormId { get; private set; }

    public DateOnly Day { get; private set; }

    /// <summary>Trap filled, values valid: kept in Spam.</summary>
    public int TrappedKept { get; private set; }

    /// <summary>Trap filled, values invalid: dropped.</summary>
    public int TrappedDropped { get; private set; }

    /// <summary>Refused by the rate limit.</summary>
    public int RateLimited { get; private set; }
}

/// <summary>The columns of <see cref="SpamCounter"/> a submit request can count in.</summary>
internal static class SpamCounterKinds
{
    public const string TrappedKept = "trapped_kept";

    public const string TrappedDropped = "trapped_dropped";

    public const string RateLimited = "rate_limited";
}

/// <summary>
/// How many submit requests one client address sent to one site in one window (018 FR-017;
/// research FS-13; data-model §1, <c>rate_windows</c>).
/// </summary>
/// <remarks>
/// Changed only through one atomic upsert, so instances share the count without a cache. The
/// key is a hash of the address, kept no longer than an hour, and nowhere else (FR-021).
/// </remarks>
internal sealed class RateWindow : ISiteOwned
{
    private RateWindow()
    {
        ClientKey = [];
        WindowKind = RateWindowKinds.Minute;
    }

    public Guid SiteId { get; private set; }

    /// <summary>SHA-256 of the client address, IPv6 grouped by /64.</summary>
    public byte[] ClientKey { get; private set; }

    /// <summary><see cref="RateWindowKinds.Minute"/> or <see cref="RateWindowKinds.Hour"/>.</summary>
    public string WindowKind { get; private set; }

    public DateTimeOffset WindowStartUtc { get; private set; }

    public int Count { get; private set; }
}

/// <summary>The two windows of the rate limit.</summary>
internal static class RateWindowKinds
{
    public const string Minute = "minute";

    public const string Hour = "hour";
}
