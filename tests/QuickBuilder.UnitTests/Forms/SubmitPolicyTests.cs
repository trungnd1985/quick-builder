using QuickBuilder.Forms.Domain;

namespace QuickBuilder.UnitTests.Forms;

/// <summary>The outcome of a submit request, in the order of data-model §4 (018 research FS-06 to FS-14).</summary>
public sealed class SubmitPolicyTests
{
    private static SubmitOutcome Decide(bool limited = false, bool live = true, bool trap = false, bool valid = true, bool duplicate = false) =>
        SubmitPolicy.Decide(new SubmitFacts(limited, live, trap, valid, duplicate));

    [Fact]
    public void Valid_values_on_a_live_form_are_accepted() =>
        Assert.Equal(SubmitOutcome.Accepted, Decide());

    [Fact]
    public void A_form_that_is_not_live_is_gone_whatever_the_values() =>
        Assert.Equal(SubmitOutcome.Gone, Decide(live: false, valid: false));

    [Fact]
    public void Invalid_values_are_refused() =>
        Assert.Equal(SubmitOutcome.Invalid, Decide(valid: false));

    [Fact]
    public void The_rate_limit_comes_before_everything() =>
        Assert.Equal(SubmitOutcome.Limited, Decide(limited: true, live: false, trap: true, valid: false, duplicate: true));

    [Fact]
    public void A_filled_trap_with_invalid_values_is_dropped_not_refused() =>
        Assert.Equal(SubmitOutcome.Dropped, Decide(trap: true, valid: false));

    [Fact]
    public void A_duplicate_is_found_before_the_trap_keeps_it_as_spam() =>
        Assert.Equal(SubmitOutcome.Duplicate, Decide(trap: true, duplicate: true));

    [Fact]
    public void A_filled_trap_with_valid_values_is_kept_as_spam() =>
        Assert.Equal(SubmitOutcome.Spam, Decide(trap: true));

    [Theory]
    [InlineData(nameof(SubmitOutcome.Accepted), true)]
    [InlineData(nameof(SubmitOutcome.Dropped), true)]
    [InlineData(nameof(SubmitOutcome.Duplicate), true)]
    [InlineData(nameof(SubmitOutcome.Spam), true)]
    [InlineData(nameof(SubmitOutcome.Invalid), false)]
    [InlineData(nameof(SubmitOutcome.Limited), false)]
    [InlineData(nameof(SubmitOutcome.Gone), false)]
    [InlineData(nameof(SubmitOutcome.Error), false)]
    public void Only_the_silent_outcomes_answer_as_a_success(string outcome, bool looksAccepted) =>
        Assert.Equal(looksAccepted, Enum.Parse<SubmitOutcome>(outcome).LooksAccepted());
}
