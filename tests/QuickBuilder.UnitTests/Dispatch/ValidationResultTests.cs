using QuickBuilder.Shared.Kernel.Validation;
using Xunit;

namespace QuickBuilder.UnitTests.Dispatch;

/// <summary>Validation results and the shape the HTTP contract expects (FR-008).</summary>
public sealed class ValidationResultTests
{
    [Fact]
    public void Valid_result_has_no_failures()
    {
        var result = ValidationResult.Valid();

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Invalid_result_carries_every_failure()
    {
        var result = ValidationResult.Invalid(
            new ValidationFailure("Name", "Required."),
            new ValidationFailure("HostNames", "At least one."));

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Failures.Count);
    }

    /// <summary>
    /// Failures group by field, matching contracts/admin-api.yaml.
    /// </summary>
    /// <remarks>
    /// The admin app reads this dictionary to put each message next to its input, so the
    /// grouping is part of the contract rather than a presentation detail. Two problems
    /// with one field must arrive together rather than one overwriting the other.
    /// </remarks>
    [Fact]
    public void Failures_for_one_field_are_grouped_together()
    {
        var result = ValidationResult.Invalid(
            new ValidationFailure("HostNames", "'a b' is not valid."),
            new ValidationFailure("HostNames", "'c d' is not valid."),
            new ValidationFailure("Name", "Required."));

        var byField = result.ToDictionary();

        Assert.Equal(2, byField.Count);
        Assert.Equal(2, byField["HostNames"].Length);
        Assert.Single(byField["Name"]);
    }

    [Fact]
    public void Valid_result_produces_an_empty_dictionary()
    {
        Assert.Empty(ValidationResult.Valid().ToDictionary());
    }

    [Fact]
    public void Invalid_accepts_an_enumerable_of_failures()
    {
        var failures = Enumerable
            .Range(0, 3)
            .Select(i => new ValidationFailure($"Field{i}", "Bad."));

        var result = ValidationResult.Invalid(failures);

        Assert.Equal(3, result.Failures.Count);
    }
}
