using QuickBuilder.Cms.Domain;
using static QuickBuilder.UnitTests.Cms.SchemaTestKit;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// The fingerprint a confirmation carries: stable for the same changes and counts, different
/// when either differs, blind to examples (014 research CS-08).
/// </summary>
public sealed class ImpactDigestTests
{
    private static readonly FieldChange Removal = new(ChangeKind.FieldRemoved) { Before = Text("f2", "minutes") };

    private static ImpactSummary Summary(int cleared, params ImpactExample[] examples) =>
        new([new ChangeImpact(Removal, ChangeRule.Lossy)
        {
            Counts = new Dictionary<ValueOutcome, int> { [ValueOutcome.Cleared] = cleared },
            Examples = examples,
        }], 300);

    [Fact]
    public void The_same_changes_and_counts_give_the_same_lower_case_hex_digest()
    {
        var digest = ImpactDigest.Compute(Summary(212));

        Assert.Equal(digest, ImpactDigest.Compute(Summary(212)));
        Assert.Matches("^[0-9a-f]{64}$", digest);
    }

    [Fact]
    public void Examples_and_the_item_count_do_not_change_it()
    {
        Assert.Equal(
            ImpactDigest.Compute(Summary(212)),
            ImpactDigest.Compute(Summary(212, new ImpactExample(Guid.NewGuid(), "x", ValueOutcome.Cleared)) with { ItemCount = 301 }));
    }

    [Fact]
    public void A_different_count_or_change_changes_it()
    {
        Assert.NotEqual(ImpactDigest.Compute(Summary(212)), ImpactDigest.Compute(Summary(213)));

        var other = new ImpactSummary([new ChangeImpact(Removal with { Before = Text("f3", "minutes") }, ChangeRule.Lossy)
        {
            Counts = new Dictionary<ValueOutcome, int> { [ValueOutcome.Cleared] = 212 },
        }], 300);
        Assert.NotEqual(ImpactDigest.Compute(Summary(212)), ImpactDigest.Compute(other));
    }

    [Fact]
    public void Counts_are_digested_in_one_order_whatever_order_they_were_added_in()
    {
        var change = new FieldChange(ChangeKind.TypeChanged) { Before = Text("f1"), After = Of(FieldType.Number, "f1") };
        var forward = new ChangeImpact(change, ChangeRule.Lossy) { Counts = new Dictionary<ValueOutcome, int> { [ValueOutcome.Converted] = 1, [ValueOutcome.Cleared] = 2 } };
        var backward = new ChangeImpact(change, ChangeRule.Lossy) { Counts = new Dictionary<ValueOutcome, int> { [ValueOutcome.Cleared] = 2, [ValueOutcome.Converted] = 1 } };

        Assert.Equal(ImpactDigest.Compute(new ImpactSummary([forward], 3)), ImpactDigest.Compute(new ImpactSummary([backward], 3)));
    }
}
