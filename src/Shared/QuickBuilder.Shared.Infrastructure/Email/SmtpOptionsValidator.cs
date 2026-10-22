using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>
/// Stops startup on settings that cannot work, naming the key (constitution Principle II), and
/// refuses to sign in without encryption to another machine (Principle IX).
/// </summary>
public sealed partial class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (!Address().IsMatch(options.From))
        {
            failures.Add("Email:Smtp:From must be the sender's email address, such as site@example.com, when Email:Smtp:Host is set.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"Email:Smtp:Port must be between 1 and 65535; it is {options.Port}.");
        }

        if (options.Timeout < SmtpOptions.MinTimeout || options.Timeout > SmtpOptions.MaxTimeout)
        {
            failures.Add($"Email:Smtp:Timeout must be between {SmtpOptions.MinTimeout} and {SmtpOptions.MaxTimeout}; it is {options.Timeout}.");
        }

        if (options.FromName.Length > SmtpOptions.MaxFromNameLength)
        {
            failures.Add($"Email:Smtp:FromName must be at most {SmtpOptions.MaxFromNameLength} characters.");
        }

        if (options.Security == SmtpSecurity.None && !string.IsNullOrEmpty(options.UserName) && !IsLoopback(options.Host))
        {
            failures.Add(
                "Email:Smtp:Security is None while Email:Smtp:UserName is set, so the password would cross the network " +
                "unencrypted. Use StartTls or SslOnConnect, or a mail server on this machine (localhost).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    // A plain address: no display name, no spaces, one @, a dot in the domain.
    [GeneratedRegex(@"^[^\s@<>""]+@[^\s@<>""]+\.[^\s@<>""]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Address();
}
