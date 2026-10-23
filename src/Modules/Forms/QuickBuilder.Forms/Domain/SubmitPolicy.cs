namespace QuickBuilder.Forms.Domain;

/// <summary>
/// What becomes of one submit request (018 data-model §4; research FS-06 to FS-14): the
/// checks in their order, the first that applies deciding.
/// </summary>
/// <remarks>
/// A body that is too large or not urlencoded never gets here: the endpoint refuses it first.
/// A failure to store is the caller's (<see cref="SubmitOutcome.Error"/>), because nothing of
/// the submission is kept when its transaction fails.
/// </remarks>
internal static class SubmitPolicy
{
    public static SubmitOutcome Decide(SubmitFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.RateLimited)
        {
            return SubmitOutcome.Limited;
        }

        if (!facts.FormLive)
        {
            return SubmitOutcome.Gone;
        }

        if (facts.TrapFilled && !facts.ValuesValid)
        {
            return SubmitOutcome.Dropped;
        }

        if (!facts.ValuesValid)
        {
            return SubmitOutcome.Invalid;
        }

        if (facts.Duplicate)
        {
            return SubmitOutcome.Duplicate;
        }

        return facts.TrapFilled ? SubmitOutcome.Spam : SubmitOutcome.Accepted;
    }
}

/// <summary>What is known about a submit request when its outcome is decided.</summary>
internal sealed record SubmitFacts(bool RateLimited, bool FormLive, bool TrapFilled, bool ValuesValid, bool Duplicate);

/// <summary>The outcomes of data-model §4.</summary>
internal enum SubmitOutcome
{
    /// <summary>Stored in the inbox; the visitor sees success.</summary>
    Accepted,

    /// <summary>Over a rate window: refused, nothing stored.</summary>
    Limited,

    /// <summary>The form is not on the live page: refused with the error message.</summary>
    Gone,

    /// <summary>Values break the form's rules: refused with every failing field.</summary>
    Invalid,

    /// <summary>The trap was filled and the values are invalid: dropped, answered as success.</summary>
    Dropped,

    /// <summary>The same values within 10 seconds: nothing new stored, answered as success.</summary>
    Duplicate,

    /// <summary>The trap was filled and the values are valid: kept in Spam, answered as success.</summary>
    Spam,

    /// <summary>The submission could not be stored: refused with the error message.</summary>
    Error,
}

internal static class SubmitOutcomes
{
    /// <summary>Whether the visitor is answered exactly as for a stored submission (FR-016, FR-018).</summary>
    public static bool LooksAccepted(this SubmitOutcome outcome) =>
        outcome is SubmitOutcome.Accepted or SubmitOutcome.Dropped or SubmitOutcome.Duplicate or SubmitOutcome.Spam;
}
