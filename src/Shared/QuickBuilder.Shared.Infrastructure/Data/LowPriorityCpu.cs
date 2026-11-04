using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace QuickBuilder.Shared.Infrastructure.Data;

/// <summary>
/// Runs the CPU-heavy steps of background jobs, such as hashing a site archive, on one dedicated
/// thread of low priority, so visitors' requests on the shared thread pool come first (020 SC-002,
/// constitution Principle VIII).
/// </summary>
/// <remarks>
/// The thread asks the operating system for a lower priority: <see cref="Thread.Priority"/> on
/// Windows, and a nice value of 10 on Linux, where .NET ignores thread priorities. Where neither
/// applies, the work still runs on one thread only, so it uses at most one CPU and holds no thread
/// of the pool. Work is queued in order; callers await it.
/// </remarks>
public static class LowPriorityCpu
{
    private const int HashBufferSize = 1024 * 1024;

    private static readonly LowPriorityScheduler Scheduler = new();

    /// <summary>Runs <paramref name="work"/> on the low-priority thread.</summary>
    public static Task RunAsync(Action work, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(work, cancellationToken, TaskCreationOptions.DenyChildAttach, Scheduler);

    /// <summary>
    /// The SHA-256 of <paramref name="content"/>: reads stay asynchronous on the caller's side,
    /// and hashing runs on the low-priority thread.
    /// </summary>
    public static async Task<byte[]> Sha256Async(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        try
        {
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, HashBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                var count = read;
                await RunAsync(() => hash.AppendData(buffer, 0, count), cancellationToken).ConfigureAwait(false);
            }

            return hash.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class LowPriorityScheduler : TaskScheduler
    {
        private readonly BlockingCollection<Task> _queue = [];

        public LowPriorityScheduler()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "QuickBuilder low-priority CPU" };
            thread.Start();
        }

        public override int MaximumConcurrencyLevel => 1;

        protected override void QueueTask(Task task) => _queue.Add(task);

        // Never inline: the point is to keep this work off the caller's thread.
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        protected override IEnumerable<Task> GetScheduledTasks() => [.. _queue];

        private void Run()
        {
            LowerPriority();
            foreach (var task in _queue.GetConsumingEnumerable())
            {
                TryExecuteTask(task);
            }
        }

        private static void LowerPriority()
        {
            if (OperatingSystem.IsWindows())
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    // Linux nice values are per thread; raising one needs no privilege.
                    _ = Native.setpriority(Native.PrioProcess, Native.gettid(), 10);
                }
                catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                {
                    // No libc to ask: the single thread still bounds the work to one CPU.
                }
            }
        }
    }

    // DllImport, not LibraryImport: these signatures need no marshalling, and LibraryImport would
    // need unsafe code in this project.
#pragma warning disable SYSLIB1054, CA5392
    private static class Native
    {
        public const int PrioProcess = 0;

        [DllImport("libc", SetLastError = true)]
        public static extern int setpriority(int which, int who, int prio);

        [DllImport("libc")]
        public static extern int gettid();
    }
#pragma warning restore SYSLIB1054, CA5392
}
