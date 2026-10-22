namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>
/// The sender when <c>Email:Smtp:Host</c> is empty: nothing is configured, so nothing is ever
/// sent and no connection is made (018 FR-035).
/// </summary>
internal sealed class DisabledEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        Task.FromResult(new EmailResult(Accepted: false, Retryable: false, ServerReply: "Outgoing email is not configured."));
}
