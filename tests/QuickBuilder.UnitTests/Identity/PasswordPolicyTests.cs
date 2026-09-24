using QuickBuilder.Identity.Infrastructure.Security;
using Xunit;

namespace QuickBuilder.UnitTests.Identity;

/// <summary>
/// The password rules of spec FR-008: length and a common-password check, no composition
/// rules (NIST SP 800-63B).
/// </summary>
public sealed class PasswordPolicyTests
{
    [Fact]
    public void Eleven_characters_are_too_few_and_twelve_are_enough()
    {
        Assert.NotEmpty(PasswordPolicy.Check("elevenchars", null, null));
        Assert.Empty(PasswordPolicy.Check("twelve chars", null, null));
    }

    [Fact]
    public void Length_is_counted_in_characters_not_utf16_units()
    {
        // Twelve emoji: 24 UTF-16 units, 12 characters.
        Assert.Empty(PasswordPolicy.Check(string.Concat(Enumerable.Repeat("\U0001F510", 12)), null, null));
        Assert.NotEmpty(PasswordPolicy.Check(string.Concat(Enumerable.Repeat("\U0001F510", 11)), null, null));
    }

    [Fact]
    public void More_than_128_characters_are_rejected()
    {
        Assert.Empty(PasswordPolicy.Check(new string('q', 128), null, null));
        Assert.NotEmpty(PasswordPolicy.Check(new string('q', 129), null, null));
    }

    [Theory]
    [InlineData("password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("qwertyuiopas")]
    public void Common_passwords_are_rejected_whatever_their_case(string password)
    {
        var failures = PasswordPolicy.Check(password, null, null);

        Assert.Single(failures);
        Assert.Contains("common", failures[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_bundled_list_loads_and_is_substantial()
    {
        // R-15: tens of thousands of entries of 12+ characters. A near-empty list would mean
        // the embedded resource went missing and the check silently passes everything.
        Assert.True(PasswordPolicy.CommonPasswordCount > 50_000);
    }

    [Fact]
    public void A_password_equal_to_the_email_or_name_is_rejected()
    {
        Assert.NotEmpty(PasswordPolicy.Check("Owner@Example.test", "owner@example.test", "Someone"));
        Assert.NotEmpty(PasswordPolicy.Check("Ada Lovelace Byron", "ada@example.test", "ada lovelace byron"));
    }

    [Theory]
    [InlineData("alllowercaseletters")]
    [InlineData("1234 5678 9012 3456 ok")]
    [InlineData("no digits no symbols here")]
    public void No_composition_rule_is_imposed(string password)
    {
        Assert.Empty(PasswordPolicy.Check(password, "someone@example.test", "Someone"));
    }

    [Fact]
    public void Failures_name_the_field_the_caller_asks_for()
    {
        var failures = PasswordPolicy.Check("short", null, null, field: "newPassword");

        Assert.Equal("newPassword", Assert.Single(failures).Field);
    }
}
