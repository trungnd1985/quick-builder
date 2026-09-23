using System.Net;
using QuickBuilder.Identity.Infrastructure.Security;
using Xunit;

namespace QuickBuilder.UnitTests.Identity;

/// <summary>
/// Setup codes, bearer tokens and client keys (002 research R-07, R-09, R-10).
/// </summary>
public sealed class TokensAndTransportTests
{
    [Fact]
    public void Setup_codes_are_twelve_unambiguous_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = Tokens.NewSetupCode();

            Assert.Equal(12, code.Length);
            Assert.DoesNotContain(code, c => c is 'I' or 'L' or 'O' or 'U');
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)));
        }
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn", "ABCDEFGHJKMN")]
    [InlineData("ABCD EFGH JKMN", "ABCDEFGHJKMN")]
    [InlineData("  abcdefghjkmn ", "ABCDEFGHJKMN")]
    [InlineData("oil0-1234-5678", "0110-1234-5678")]
    public void Setup_code_input_is_forgiving(string typed, string expected)
    {
        Assert.Equal(expected.Replace("-", string.Empty, StringComparison.Ordinal), Tokens.NormaliseSetupCode(typed));
    }

    [Fact]
    public void Setup_codes_are_shown_in_groups_of_four()
    {
        Assert.Equal("ABCD-EFGH-JKMN", Tokens.FormatSetupCode("ABCDEFGHJKMN"));
    }

    [Fact]
    public void Tokens_are_random_and_their_stored_form_is_a_sha256()
    {
        var first = Tokens.NewSessionToken();
        var second = Tokens.NewSessionToken();

        Assert.NotEqual(first, second);
        Assert.Equal(32, Tokens.Hash(first).Length);
        Assert.Equal(Tokens.Hash(first), Tokens.Hash(first));

        // base64url: safe in a cookie and in a URL fragment.
        Assert.DoesNotContain(first, c => c is '+' or '/' or '=');
    }

    [Fact]
    public void Two_addresses_in_one_ipv6_64_share_a_client_key()
    {
        var a = TransportSecurity.ClientKey(IPAddress.Parse("2001:db8:1:2:aaaa::1"));
        var b = TransportSecurity.ClientKey(IPAddress.Parse("2001:db8:1:2:ffff::9"));
        var other = TransportSecurity.ClientKey(IPAddress.Parse("2001:db8:1:3::1"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void Ipv4_addresses_are_keyed_exactly_even_when_mapped_to_ipv6()
    {
        Assert.Equal("192.0.2.10", TransportSecurity.ClientKey(IPAddress.Parse("192.0.2.10")));
        Assert.Equal("192.0.2.10", TransportSecurity.ClientKey(IPAddress.Parse("::ffff:192.0.2.10")));
        Assert.NotEqual(
            TransportSecurity.ClientKey(IPAddress.Parse("192.0.2.10")),
            TransportSecurity.ClientKey(IPAddress.Parse("192.0.2.11")));
    }

    [Fact]
    public void The_account_key_ignores_case_and_surrounding_space()
    {
        Assert.Equal(
            SignInThrottleService.AccountKey("Owner@Example.test"),
            SignInThrottleService.AccountKey("  owner@example.TEST "));
    }
}
