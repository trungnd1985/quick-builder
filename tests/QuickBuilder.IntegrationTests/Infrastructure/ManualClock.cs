namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// A clock a test moves by hand, so time-based rules — throttle waits, session expiry,
/// retention — are tested without sleeping.
/// </summary>
/// <remarks>
/// Starts at the real current time, because some rows are written with the system clock
/// (seeded sessions, for example) and must still look current.
/// </remarks>
public sealed class ManualClock : TimeProvider
{
    private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
