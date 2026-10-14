using System.Collections.Concurrent;
using QuickBuilder.Pages.Contracts.Redirects;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Counts redirect use in memory (017 FR-021a, research SE-12). Serving a redirect only adds
/// here; <see cref="RedirectUsageFlusher"/> saves the counts in the background, so the public
/// path never writes (constitution Principle VII).
/// </summary>
/// <remarks>
/// A singleton with no I/O and nothing that can throw: an architecture test holds it to that.
/// Counts not yet saved when an instance stops are lost, which the spec accepts.
/// </remarks>
internal sealed class RedirectHitCounter(TimeProvider clock) : IRedirectHits
{
    private ConcurrentDictionary<(Guid SiteId, Guid RedirectId), Tally> _tallies = new();

    public void Record(Guid siteId, Guid redirectId)
    {
        var tally = _tallies.GetOrAdd((siteId, redirectId), static _ => new Tally());
        tally.Add(clock.GetUtcNow());
    }

    /// <summary>The counts since the last call, which start again from zero.</summary>
    public IReadOnlyList<RedirectHitTally> Drain()
    {
        var drained = Interlocked.Exchange(ref _tallies, new ConcurrentDictionary<(Guid SiteId, Guid RedirectId), Tally>());
        return [.. drained.Select(t => new RedirectHitTally(t.Key.SiteId, t.Key.RedirectId, t.Value.Hits, t.Value.LastUsedAtUtc))];
    }

    private sealed class Tally
    {
        private long _hits;
        private long _lastTicks;

        public long Hits => Interlocked.Read(ref _hits);

        public DateTimeOffset LastUsedAtUtc => new(Interlocked.Read(ref _lastTicks), TimeSpan.Zero);

        public void Add(DateTimeOffset at)
        {
            Interlocked.Increment(ref _hits);
            var ticks = at.UtcTicks;
            long seen;
            while ((seen = Interlocked.Read(ref _lastTicks)) < ticks
                && Interlocked.CompareExchange(ref _lastTicks, ticks, seen) != seen)
            {
            }
        }
    }
}

/// <summary>How often a redirect was followed since the last flush, and when last.</summary>
internal sealed record RedirectHitTally(Guid SiteId, Guid RedirectId, long Hits, DateTimeOffset LastUsedAtUtc);
