using QuickBuilder.Shared.Infrastructure.Email;

namespace QuickBuilder.UnitTests.Email;

/// <summary>
/// The SMTP settings are checked at startup (018 module-surface.md §5; research FS-18): email is
/// off without a host, and a configured host must name a valid sender and never send a password
/// in clear text to another machine.
/// </summary>
public sealed class SmtpOptionsValidatorTests
{
    private static SmtpOptions Valid() => new() { Host = "smtp.example.com", From = "site@example.com", UserName = "user", Password = "secret" };

    private static string? Failure(SmtpOptions options)
    {
        var result = new SmtpOptionsValidator().Validate(null, options);
        return result.Failed ? result.FailureMessage : null;
    }

    [Fact]
    public void No_host_means_email_is_off_and_nothing_else_is_checked() =>
        Assert.Null(Failure(new SmtpOptions { Port = 0, From = "not an address" }));

    [Fact]
    public void A_complete_configuration_is_valid() => Assert.Null(Failure(Valid()));

    [Fact]
    public void A_host_without_a_sender_names_the_key()
    {
        var options = Valid();
        options.From = "";

        Assert.Contains("Email:Smtp:From", Failure(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("a@b")]
    [InlineData("Site <site@example.com>")]
    public void The_sender_must_be_a_plain_address(string from)
    {
        var options = Valid();
        options.From = from;

        Assert.Contains("Email:Smtp:From", Failure(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void The_port_is_in_range(int port)
    {
        var options = Valid();
        options.Port = port;

        Assert.Contains("Email:Smtp:Port", Failure(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:04")]
    [InlineData("00:05:01")]
    public void The_timeout_is_between_five_seconds_and_five_minutes(string timeout)
    {
        var options = Valid();
        options.Timeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("Email:Smtp:Timeout", Failure(options), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sender_name_is_at_most_100_characters()
    {
        var options = Valid();
        options.FromName = new string('a', 101);

        Assert.Contains("Email:Smtp:FromName", Failure(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_in_without_encryption_is_refused_for_another_machine()
    {
        var options = Valid();
        options.Security = SmtpSecurity.None;

        Assert.Contains("Email:Smtp:Security", Failure(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Signing_in_without_encryption_is_allowed_on_this_machine(string host)
    {
        var options = Valid();
        options.Security = SmtpSecurity.None;
        options.Host = host;

        Assert.Null(Failure(options));
    }

    [Fact]
    public void No_encryption_without_signing_in_is_allowed_anywhere()
    {
        var options = Valid();
        options.Security = SmtpSecurity.None;
        options.UserName = "";
        options.Password = "";

        Assert.Null(Failure(options));
    }
}
