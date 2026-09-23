namespace QuickBuilder.Identity.Domain;

/// <summary>
/// Whether this installation has been claimed by its first administrator (data-model.md,
/// <c>installation</c>).
/// </summary>
/// <remarks>
/// Exactly one row. While unclaimed it holds the single setup code every instance prints
/// (research R-09). Claiming is one-way: once claimed, the code is gone and setup can never
/// run again (spec FR-003). The claim itself happens in SQL as a conditional update so that
/// concurrent submissions cannot both win (research R-12); <see cref="Claim"/> expresses the
/// same rule for the domain model and its tests.
/// </remarks>
internal sealed class Installation
{
    public const short SingletonId = 1;

    private Installation()
    {
    }

    public short Id { get; private set; } = SingletonId;

    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    /// <summary>Normalised setup code (no dashes). Non-null only while unclaimed.</summary>
    public string? SetupCode { get; private set; }

    public DateTimeOffset? SetupCodeCreatedAtUtc { get; private set; }

    public bool IsClaimed => ClaimedAtUtc.HasValue;

    public static Installation CreateUnclaimed(string setupCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupCode);

        return new Installation
        {
            SetupCode = setupCode,
            SetupCodeCreatedAtUtc = now,
        };
    }

    public void Claim(DateTimeOffset now)
    {
        if (IsClaimed)
        {
            throw new InvalidOperationException("The installation has already been claimed; setup runs exactly once.");
        }

        ClaimedAtUtc = now;
        SetupCode = null;
        SetupCodeCreatedAtUtc = null;
    }
}
