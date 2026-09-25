using QuickBuilder.Media.Infrastructure;

namespace QuickBuilder.UnitTests.Media;

/// <summary>The upload gate leaves a core for the rest of the admin surface (003 SC-008, research M-23).</summary>
public sealed class UploadGateTests
{
    [Fact]
    public void By_default_it_leaves_one_processor_free_but_always_allows_one_upload()
    {
        using var gate = new UploadGate();

        Assert.Equal(Math.Max(1, Environment.ProcessorCount - 1), gate.Concurrency);
    }

    [Fact]
    public async Task It_admits_up_to_its_limit_and_queues_the_rest_until_a_slot_frees()
    {
        using var gate = new UploadGate(2);

        var first = await gate.EnterAsync(CancellationToken.None);
        var second = await gate.EnterAsync(CancellationToken.None);
        var third = gate.EnterAsync(CancellationToken.None);

        Assert.False(third.IsCompleted);

        first.Dispose();
        using var admitted = await third.WaitAsync(TimeSpan.FromSeconds(5));
        second.Dispose();
    }

    [Fact]
    public async Task Releasing_a_slot_twice_frees_it_only_once()
    {
        using var gate = new UploadGate(1);

        var slot = await gate.EnterAsync(CancellationToken.None);
        slot.Dispose();
        slot.Dispose();

        using var again = await gate.EnterAsync(CancellationToken.None);
        var queued = gate.EnterAsync(CancellationToken.None);

        Assert.False(queued.IsCompleted);
    }

    [Fact]
    public async Task A_waiting_upload_stops_waiting_when_its_request_is_cancelled()
    {
        using var gate = new UploadGate(1);
        using var held = await gate.EnterAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var waiting = gate.EnterAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void A_limit_below_one_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new UploadGate(0));
}
