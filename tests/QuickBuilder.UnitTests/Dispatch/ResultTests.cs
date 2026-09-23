using QuickBuilder.Shared.Kernel.Results;
using Xunit;

namespace QuickBuilder.UnitTests.Dispatch;

/// <summary>
/// The three-way outcome contract (FR-009).
/// </summary>
/// <remarks>
/// Controllers map results to responses without catching anything, so the distinctions
/// here are what keep exception handling out of the HTTP layer. Worth unit testing rather
/// than only exercising through integration tests: the failure modes are about the type's
/// own invariants, not about any endpoint.
/// </remarks>
public sealed class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        var error = Error.Conflict("sites.host_name_taken", "Already assigned.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Success_of_T_carries_its_value()
    {
        var result = Result.Success("payload");

        Assert.True(result.IsSuccess);
        Assert.Equal("payload", result.Value);
    }

    /// <summary>
    /// Reading the value of a failure throws rather than returning a default.
    /// </summary>
    /// <remarks>
    /// The alternative — returning null or default — is how a failed result gets
    /// serialised into a 200 response. Throwing makes the mistake loud at the call site
    /// that made it.
    /// </remarks>
    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result.Failure<string>(Error.NotFound("sites.not_found", "No such site."));

        var thrown = Assert.Throws<InvalidOperationException>(() => result.Value);

        // The message names the underlying error, so the stack trace says why rather than
        // only that something went wrong.
        Assert.Contains("sites.not_found", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_converts_implicitly_to_a_successful_result()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Error_renders_code_and_message()
    {
        var error = Error.Validation("sites.invalid_name", "A site name is required.");

        Assert.Equal("sites.invalid_name: A site name is required.", error.ToString());
    }

    /// <summary>
    /// Error codes are contract, so equality is by value.
    /// </summary>
    /// <remarks>
    /// Clients branch on the code. Two errors describing the same condition must compare
    /// equal so that tests asserting on them do not depend on reference identity.
    /// </remarks>
    [Fact]
    public void Errors_with_the_same_code_and_message_are_equal()
    {
        var first = Error.Conflict("sites.host_name_taken", "Already assigned.");
        var second = Error.Conflict("sites.host_name_taken", "Already assigned.");

        Assert.Equal(first, second);
    }
}
