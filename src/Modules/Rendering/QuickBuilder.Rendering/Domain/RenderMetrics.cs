namespace QuickBuilder.Rendering.Domain;

/// <summary>How many pages were rendered rather than served from the cache. For tests and diagnostics.</summary>
internal sealed class RenderMetrics
{
    private long _renders;

    public long Renders => Interlocked.Read(ref _renders);

    public void Rendered() => Interlocked.Increment(ref _renders);
}
