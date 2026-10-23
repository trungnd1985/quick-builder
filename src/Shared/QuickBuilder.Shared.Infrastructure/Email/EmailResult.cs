namespace QuickBuilder.Shared.Infrastructure.Email;

/// <summary>
/// What the mail server said. <see cref="Retryable"/> is false for a permanent reply (5xx) and
/// true for a temporary one (4xx) or a connection that failed. <see cref="ServerReply"/> is the
/// reply code and text, never a credential.
/// </summary>
public sealed record EmailResult(bool Accepted, bool Retryable, string? ServerReply);
