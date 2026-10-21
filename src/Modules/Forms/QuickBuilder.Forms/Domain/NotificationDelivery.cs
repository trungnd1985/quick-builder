using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// The email notification of one submission (018 FR-034; data-model §1, §3; research FS-17).
/// </summary>
/// <remarks>
/// Holds attempts and the outcome, never recipients or content: the worker builds the message
/// from the submission and the form's last published settings at each attempt. A claim marks it
/// <see cref="DeliveryStates.Sending"/> under a lease before the message leaves, so a delivery
/// whose instance died mid-send is failed as interrupted rather than sent twice.
/// </remarks>
internal sealed class NotificationDelivery : ISiteOwned
{
    /// <summary>How long a claim lasts before the delivery counts as interrupted.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>The longest stored reason.</summary>
    public const int MaxErrorLength = 500;

    public const string InterruptedReason = "interrupted";

    private NotificationDelivery()
    {
        State = DeliveryStates.Pending;
    }

    /// <summary>The waits before the second to sixth attempts (research FS-17).</summary>
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60),
    ];

    public static int MaxAttempts => RetryDelays.Count + 1;

    public Guid SubmissionId { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>One of <see cref="DeliveryStates"/>.</summary>
    public string State { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? LeaseUntilUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    /// <summary>The submission state this delivery state shows as (data-model §3).</summary>
    public string SubmissionState => State switch
    {
        DeliveryStates.Sent => NotificationStates.Sent,
        DeliveryStates.Failed => NotificationStates.Failed,
        _ => NotificationStates.Pending,
    };

    public static NotificationDelivery For(Submission submission, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new NotificationDelivery
        {
            SubmissionId = submission.Id,
            SiteId = submission.SiteId,
            NextAttemptAtUtc = nowUtc,
        };
    }

    /// <summary>Starts an attempt: sending, under a lease.</summary>
    public void Claim(DateTimeOffset nowUtc)
    {
        State = DeliveryStates.Sending;
        Attempts++;
        LeaseUntilUtc = nowUtc + Lease;
        NextAttemptAtUtc = null;
    }

    public void MarkSent(DateTimeOffset nowUtc)
    {
        State = DeliveryStates.Sent;
        SentAtUtc = nowUtc;
        LeaseUntilUtc = null;
        LastError = null;
    }

    /// <summary>
    /// Records a refused attempt: waits for the next one when the refusal may pass and attempts
    /// remain, else fails for good.
    /// </summary>
    public void MarkRefused(string reason, bool retryable, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reason);

        LastError = reason.Length <= MaxErrorLength ? reason : reason[..MaxErrorLength];
        LeaseUntilUtc = null;

        if (retryable && Attempts < MaxAttempts)
        {
            State = DeliveryStates.Pending;
            NextAttemptAtUtc = nowUtc + RetryDelays[Attempts - 1];
            return;
        }

        State = DeliveryStates.Failed;
        NextAttemptAtUtc = null;
    }

    /// <summary>A claim whose lease ran out: the message may have left, so it is never sent again.</summary>
    public void MarkInterrupted()
    {
        State = DeliveryStates.Failed;
        LastError = InterruptedReason;
        LeaseUntilUtc = null;
        NextAttemptAtUtc = null;
    }
}

/// <summary>The states of a <see cref="NotificationDelivery"/>.</summary>
internal static class DeliveryStates
{
    public const string Pending = "pending";

    public const string Sending = "sending";

    public const string Sent = "sent";

    public const string Failed = "failed";
}
