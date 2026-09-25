namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Limits how many uploads stream their bytes into storage at once (003 SC-008, research M-23).
/// </summary>
/// <remarks>
/// Found by the performance run: ten 20 MiB uploads at full speed kept both of the reference
/// environment's CPUs busy receiving and writing bytes, and every other admin request slowed
/// from p95 5 ms to 55 ms — past SC-008's 2× limit. Letting at most
/// <c>ProcessorCount - 1</c> uploads write at once always leaves a core for everything else,
/// the same trade 002 made for password hashing (research R-19). An upload waiting its turn
/// has not read its body yet, so TCP backpressure holds the client instead of the server
/// buffering anything.
/// </remarks>
internal sealed class UploadGate : IDisposable
{
    private readonly SemaphoreSlim _slots;

    public UploadGate()
        : this(Math.Max(1, Environment.ProcessorCount - 1))
    {
    }

    internal UploadGate(int concurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        Concurrency = concurrency;
        _slots = new SemaphoreSlim(concurrency, concurrency);
    }

    /// <summary>How many uploads may write at once.</summary>
    public int Concurrency { get; }

    /// <summary>Waits for a slot. Dispose the result to release it.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Slot(_slots);
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}
