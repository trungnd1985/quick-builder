namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Told after each batch of items a schema change rewrites (research CS-20).
/// </summary>
/// <remarks>
/// Production registers <see cref="NoSchemaMigrationObserver"/>. Tests register one that
/// throws or pauses, to prove that an interrupted change leaves everything as it was
/// (SC-005) and that item writes are refused while a change runs (FR-037).
/// </remarks>
internal interface ISchemaMigrationObserver
{
    Task AfterBatchAsync(int batchNumber, CancellationToken cancellationToken);
}

/// <summary>Does nothing.</summary>
internal sealed class NoSchemaMigrationObserver : ISchemaMigrationObserver
{
    public Task AfterBatchAsync(int batchNumber, CancellationToken cancellationToken) => Task.CompletedTask;
}
