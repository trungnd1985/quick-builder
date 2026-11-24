using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>How the mail server is reached (018 module-surface.md §5).</summary>
public enum SmtpSecurity
{
    /// <summary>No encryption. Signing in this way is allowed only on this machine.</summary>
    None,

    /// <summary>Connect in clear text, then require STARTTLS before anything is sent.</summary>
    StartTls,

    /// <summary>TLS from the first byte, usually on port 465.</summary>
    SslOnConnect,
}

/// <summary>
/// The <c>Email:Smtp</c> settings (018 research FS-18). All optional: an empty
/// <see cref="Host"/> turns email off. The password comes from a secret (environment or file),
/// never from the database.
/// </summary>
[SettingsSection(SectionName, Area = "email", Title = "Email")]
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public const int MaxFromNameLength = 100;

    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);

    /// <summary>The mail server's host name or address. Empty means email is off.</summary>
    [Setting("The mail server's host name or address. Empty turns email off: no connection is made and editors see that notifications are off.")]
    public string Host { get; set; } = string.Empty;

    [Setting("The mail server's port. Usually 587 with `StartTls`, 465 with `SslOnConnect`.", Allowed = "1 – 65535")]
    public int Port { get; set; } = 587;

    [Setting("How the connection is encrypted. `StartTls` encrypts before anything is sent and the server must offer it; `SslOnConnect` uses TLS from the first byte. The server's certificate must be trusted by the container. `None` with a user name is allowed only for a mail server on the same machine.")]
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    [Setting("The sign-in name. Empty means no sign-in.")]
    public string UserName { get; set; } = string.Empty;

    [Setting("The sign-in password. It is never stored in the database or shown in the admin.", Secret = true)]
    public string Password { get; set; } = string.Empty;

    /// <summary>The sender address; required when <see cref="Host"/> is set.</summary>
    [Setting("The sender address, such as `website@example.com`. Required when `Email:Smtp:Host` is set.", Format = "an email address")]
    public string From { get; set; } = string.Empty;

    [Setting("The sender name shown to recipients.", Allowed = "at most 100 characters")]
    public string FromName { get; set; } = "QuickBuilder";

    [Setting("How long one sending attempt may take.", Allowed = "`00:00:05` – `00:05:00`")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether a host is set, which is what turns email on.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
