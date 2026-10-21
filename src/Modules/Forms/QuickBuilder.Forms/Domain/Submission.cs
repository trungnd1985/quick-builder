using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// One accepted submission of a form (018 FR-020; data-model §1, <c>submissions</c>, §3).
/// </summary>
/// <remarks>
/// Holds what the visitor entered and nothing about the visitor: no address, browser or cookie
/// (FR-021). Values are copied with each field's label and type as they were when received, so
/// renaming or removing a field later never changes what was submitted.
/// </remarks>
internal sealed class Submission : ISiteOwned
{
    /// <summary>The longest source address stored.</summary>
    public const int MaxPagePathLength = 2048;

    private Submission()
    {
        FormId = string.Empty;
        FormName = string.Empty;
        PagePath = string.Empty;
        Values = "[]";
        SearchText = string.Empty;
        Fingerprint = [];
        NotificationState = NotificationStates.NotNeeded;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string FormId { get; private set; }

    /// <summary>The form's name when received, shown when the form is gone.</summary>
    public string FormName { get; private set; }

    /// <summary>The page that served the form; a template's id for item pages.</summary>
    public Guid PageId { get; private set; }

    public string PagePath { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    /// <summary>JSON: <c>[{ "fieldId", "label", "type", "value" }]</c>, in field order.</summary>
    public string Values { get; private set; }

    public string SearchText { get; private set; }

    /// <summary>SHA-256 of the form id and the normalised values (research FS-14).</summary>
    public byte[] Fingerprint { get; private set; }

    public bool IsRead { get; private set; }

    public bool IsSpam { get; private set; }

    /// <summary>One of <see cref="NotificationStates"/>.</summary>
    public string NotificationState { get; private set; }

    /// <summary>A submission as received: unread, in the inbox, or in Spam when the trap was filled.</summary>
    public static Submission Receive(Guid siteId, SubmissionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new Submission
        {
            Id = Guid.CreateVersion7(receipt.ReceivedAtUtc),
            SiteId = siteId,
            FormId = receipt.FormId,
            FormName = receipt.FormName,
            PageId = receipt.PageId,
            PagePath = receipt.PagePath,
            ReceivedAtUtc = receipt.ReceivedAtUtc,
            Values = receipt.ValuesJson,
            SearchText = receipt.SearchText,
            Fingerprint = receipt.Fingerprint,
            IsSpam = receipt.IsSpam,
            NotificationState = receipt.IsSpam ? NotificationStates.None : receipt.NotificationState,
        };
    }

    public void MarkRead() => IsRead = true;

    public void MarkUnread() => IsRead = false;

    /// <summary>
    /// Moves a spam submission to the inbox, unread (FR-016a). False when it is not spam, which
    /// the caller refuses as <c>forms.not_spam</c>.
    /// </summary>
    public bool Restore(string notificationState)
    {
        if (!IsSpam)
        {
            return false;
        }

        IsSpam = false;
        IsRead = false;
        NotificationState = notificationState;
        return true;
    }

    /// <summary>Mirrors the delivery's state for lists and filters (data-model §3).</summary>
    public void SetNotificationState(string state) => NotificationState = state;
}

/// <summary>Everything a new <see cref="Submission"/> is made of, apart from its site.</summary>
internal sealed record SubmissionReceipt(
    string FormId,
    string FormName,
    Guid PageId,
    string PagePath,
    DateTimeOffset ReceivedAtUtc,
    string ValuesJson,
    string SearchText,
    byte[] Fingerprint,
    bool IsSpam,
    string NotificationState);

/// <summary>The notification states a submission shows (018 FR-035; data-model §3).</summary>
internal static class NotificationStates
{
    /// <summary>The form has no recipients.</summary>
    public const string NotNeeded = "not_needed";

    /// <summary>Email is not configured on the installation.</summary>
    public const string NotSent = "not_sent";

    public const string Pending = "pending";

    public const string Sent = "sent";

    public const string Failed = "failed";

    /// <summary>Spam: never notified unless restored.</summary>
    public const string None = "none";

    public static IReadOnlyList<string> All { get; } = [NotNeeded, NotSent, Pending, Sent, Failed, None];

    /// <summary>The state a new or restored submission starts in.</summary>
    public static string Initial(bool hasRecipients, bool emailConfigured) =>
        !hasRecipients ? NotNeeded : emailConfigured ? Pending : NotSent;
}
