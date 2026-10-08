using QuickBuilder.Cms.Domain;

namespace QuickBuilder.UnitTests.Cms.Items;

/// <summary>History ids increase within one millisecond, in .NET and in PostgreSQL order (015 FR-031).</summary>
public sealed class MonotonicIdsTests
{
    [Fact]
    public void Ids_of_one_moment_increase_and_stay_version_7()
    {
        var at = DateTimeOffset.UtcNow.AddYears(1);
        var ids = Enumerable.Range(0, 300).Select(_ => MonotonicIds.Next(at)).ToList();

        Assert.Equal(ids, ids.Order().ToList());
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(7, id.Version));
        Assert.All(ids, id => Assert.Equal(0b10, id.Variant >> 2));

        // PostgreSQL compares uuids byte by byte, big-endian.
        var bytes = ids.Select(id => id.ToByteArray(bigEndian: true)).ToList();
        for (var i = 1; i < bytes.Count; i++)
        {
            Assert.True(bytes[i - 1].AsSpan().SequenceCompareTo(bytes[i]) < 0);
        }
    }

    [Fact]
    public void A_later_moment_starts_after_the_earlier_ones()
    {
        var at = DateTimeOffset.UtcNow.AddYears(2);
        var first = MonotonicIds.Next(at);
        var later = MonotonicIds.Next(at.AddMilliseconds(1));
        Assert.True(first.CompareTo(later) < 0);
    }
}
