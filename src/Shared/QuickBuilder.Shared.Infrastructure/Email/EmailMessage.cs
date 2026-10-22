namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>
/// One plain-text message. Recipients and the reply address are plain addresses; the subject
/// loses any line break before it becomes a header.
/// </summary>
public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string TextBody, string? ReplyTo);
