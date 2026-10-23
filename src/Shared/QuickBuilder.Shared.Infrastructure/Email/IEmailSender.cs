namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>
/// Sends plain-text email through the operator's mail server (018 research FS-18;
/// constitution Principle IV lists email among the pluggable providers).
/// </summary>
public interface IEmailSender
{
    /// <summary>Whether outgoing email is set up. When false, nothing is ever sent.</summary>
    bool IsConfigured { get; }

    /// <summary>Hands <paramref name="message"/> to the mail server, once.</summary>
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
