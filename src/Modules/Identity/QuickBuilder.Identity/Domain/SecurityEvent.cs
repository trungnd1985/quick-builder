using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickBuilder.Identity.Domain.Details;

namespace QuickBuilder.Identity.Domain;

/// <summary>The event catalogue (data-model.md, "Event catalogue"; spec FR-029).</summary>
internal static class SecurityEventTypes
{
    public const string SetupCompleted = "setup_completed";
    public const string SetupCodeRejected = "setup_code_rejected";
    public const string SignIn = "sign_in";
    public const string ClientBlocked = "client_blocked";
    public const string SignOut = "sign_out";
    public const string SessionsRevoked = "sessions_revoked";
    public const string PasswordChanged = "password_changed";
    public const string AccountCreated = "account_created";
    public const string EmailChanged = "email_changed";
    public const string DisplayNameChanged = "display_name_changed";
    public const string AccountDeactivated = "account_deactivated";
    public const string AccountReactivated = "account_reactivated";
    public const string GrantsChanged = "grants_changed";
    public const string SetPasswordLinkIssued = "set_password_link_issued";
    public const string SetPasswordLinkUsed = "set_password_link_used";
    public const string KnownDeviceRevoked = "known_device_revoked";
    public const string OperatorRecoveryUsed = "operator_recovery_used";

    // 009 FR-028: site changes that alter what the public can reach, recorded from the
    // Sites module's integration events (research S-11).
    public const string SiteHostNameAdded = "site_host_name_added";
    public const string SiteHostNameRemoved = "site_host_name_removed";
    public const string SiteDisabled = "site_disabled";
    public const string SiteEnabled = "site_enabled";

    // 020 research ST-19: site export and import, recorded from the Sites module's events.
    public const string SiteExported = "site_exported";
    public const string SiteImported = "site_imported";
    public const string SiteRestored = "site_restored";
    public const string SiteTransferFailed = "site_transfer_failed";
    public const string SiteTransferCancelled = "site_transfer_cancelled";
    public const string SiteArchiveDownloaded = "site_archive_downloaded";
    public const string SiteArchiveDeleted = "site_archive_deleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SetupCompleted, SetupCodeRejected, SignIn, ClientBlocked, SignOut, SessionsRevoked,
        PasswordChanged, AccountCreated, EmailChanged, DisplayNameChanged, AccountDeactivated,
        AccountReactivated, GrantsChanged, SetPasswordLinkIssued, SetPasswordLinkUsed,
        KnownDeviceRevoked, OperatorRecoveryUsed,
        SiteHostNameAdded, SiteHostNameRemoved, SiteDisabled, SiteEnabled,
        SiteExported, SiteImported, SiteRestored, SiteTransferFailed, SiteTransferCancelled,
        SiteArchiveDownloaded, SiteArchiveDeleted,
    };
}

internal static class SecurityEventOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Refused = "refused";
}

/// <summary>
/// A durable, append-only record of an authentication- or authorisation-relevant action
/// (spec FR-029; research R-14).
/// </summary>
/// <remarks>
/// No public setters, and <c>IdentityDbContext</c> refuses to save a modified or deleted
/// event. The retention sweep's set-based delete is the only way a row ever leaves.
/// </remarks>
internal sealed class SecurityEvent
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private SecurityEvent()
    {
        EventType = string.Empty;
        Outcome = string.Empty;
        Details = "{}";
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string EventType { get; private set; }

    public string Outcome { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public Guid? SubjectUserId { get; private set; }

    public IPAddress? ClientAddress { get; private set; }

    /// <summary>JSON text, stored as <c>jsonb</c>.</summary>
    public string Details { get; private set; }

    public static SecurityEvent Create(
        string eventType,
        string outcome,
        DateTimeOffset now,
        Guid? actorUserId,
        Guid? subjectUserId,
        IPAddress? clientAddress,
        SecurityEventDetails? details = null)
    {
        if (!SecurityEventTypes.All.Contains(eventType))
        {
            throw new ArgumentException($"'{eventType}' is not in the security event catalogue.", nameof(eventType));
        }

        if (outcome is not (SecurityEventOutcomes.Succeeded or SecurityEventOutcomes.Failed or SecurityEventOutcomes.Refused))
        {
            throw new ArgumentException($"'{outcome}' is not a security event outcome.", nameof(outcome));
        }

        return new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAtUtc = now,
            EventType = eventType,
            Outcome = outcome,
            ActorUserId = actorUserId,
            SubjectUserId = subjectUserId,
            ClientAddress = clientAddress,
            Details = details is null ? "{}" : JsonSerializer.Serialize(details, details.GetType(), DetailsJson),
        };
    }
}
