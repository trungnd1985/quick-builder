using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// A schema migration that can be stopped or held after a given batch (research CS-20): to
/// prove that an interrupted change leaves everything as it was (SC-005), and that other
/// work is refused while a change holds the collection (FR-037).
/// </summary>
internal sealed class TestMigrationObserver : ISchemaMigrationObserver, IDisposable
{
    private readonly SemaphoreSlim _reached = new(0);
    private readonly SemaphoreSlim _release = new(0);

    /// <summary>Throw after this batch; null never throws.</summary>
    public int? FailAfterBatch { get; set; }

    /// <summary>Hold after this batch until <see cref="Release"/>; null never holds.</summary>
    public int? HoldAfterBatch { get; set; }

    public async Task AfterBatchAsync(int batchNumber, CancellationToken cancellationToken)
    {
        if (batchNumber == FailAfterBatch)
        {
            throw new InvalidOperationException($"Test fault after batch {batchNumber}.");
        }

        if (batchNumber == HoldAfterBatch)
        {
            _reached.Release();
            await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until the migration is held.</summary>
    public Task WaitUntilHeldAsync() => _reached.WaitAsync(TimeSpan.FromSeconds(60));

    public void Release() => _release.Release();

    public void Dispose()
    {
        _reached.Dispose();
        _release.Dispose();
    }

    /// <summary>Replaces the no-op observer with <paramref name="observer"/>.</summary>
    public static Action<IServiceCollection> Install(TestMigrationObserver observer) => services =>
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(ISchemaMigrationObserver)).ToList())
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<ISchemaMigrationObserver>(observer);
    };
}
