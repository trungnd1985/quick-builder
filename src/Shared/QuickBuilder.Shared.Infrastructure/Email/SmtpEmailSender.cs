using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>
/// Sends through the operator's SMTP server with MailKit (018 research FS-18): one connection
/// per message, the configured security mode, and a timeout over the whole exchange.
/// </summary>
/// <remarks>
/// Headers are built through MimeKit's typed API, never from text, and the subject loses its
/// line breaks first, so no value can add a header. A reply is classified by its code: 4xx and
/// connection failures may be retried, 5xx may not. The reply text is the server's, which
/// never contains the password.
/// </remarks>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _settings = options.Value;

    public bool IsConfigured => true;

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mime = Build(message);
        using var client = new SmtpClient { Timeout = (int)_settings.Timeout.TotalMilliseconds };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_settings.Timeout);

        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, SocketOptions(_settings.Security), timeout.Token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(_settings.UserName))
            {
                await client.AuthenticateAsync(_settings.UserName, _settings.Password, timeout.Token).ConfigureAwait(false);
            }

            var reply = await client.SendAsync(mime, timeout.Token).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, timeout.Token).ConfigureAwait(false);
            return new EmailResult(Accepted: true, Retryable: false, ServerReply: reply);
        }
        catch (SmtpCommandException ex)
        {
            var code = (int)ex.StatusCode;
            return new EmailResult(Accepted: false, Retryable: code < 500, ServerReply: $"{code} {ex.Message}");
        }
        catch (Exception ex) when (ex is AuthenticationException or NotSupportedException)
        {
            // Wrong credentials, or a server without the sign-in or STARTTLS the settings ask
            // for, stay that way until the operator changes the settings.
            return new EmailResult(Accepted: false, Retryable: false, ServerReply: ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EmailResult(Accepted: false, Retryable: true, ServerReply: $"No answer from the mail server within {_settings.Timeout}.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or SmtpProtocolException or SslHandshakeException or ServiceNotConnectedException or TimeoutException)
        {
            return new EmailResult(Accepted: false, Retryable: true, ServerReply: ex.Message);
        }
    }

    private MimeMessage Build(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_settings.FromName, _settings.From));
        foreach (var to in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        if (message.ReplyTo is { } replyTo)
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        mime.Subject = OneLine(message.Subject);
        mime.Body = new TextPart(TextFormat.Plain) { Text = message.TextBody };
        return mime;
    }

    /// <summary>The text with every line break replaced by a space, for a header.</summary>
    internal static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static SecureSocketOptions SocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };
}
