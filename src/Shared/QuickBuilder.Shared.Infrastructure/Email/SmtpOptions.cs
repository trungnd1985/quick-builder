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
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public const int MaxFromNameLength = 100;

    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);

    /// <summary>The mail server's host name or address. Empty means email is off.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>The sender address; required when <see cref="Host"/> is set.</summary>
    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = "QuickBuilder";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether a host is set, which is what turns email on.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
