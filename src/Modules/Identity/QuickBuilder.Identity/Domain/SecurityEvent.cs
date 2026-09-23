using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SetupCompleted, SetupCodeRejected, SignIn, ClientBlocked, SignOut, SessionsRevoked,
        PasswordChanged, AccountCreated, EmailChanged, DisplayNameChanged, AccountDeactivated,
        AccountReactivated, GrantsChanged, SetPasswordLinkIssued, SetPasswordLinkUsed,
        KnownDeviceRevoked, OperatorRecoveryUsed,
    };
}

internal static class SecurityEventOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Refused = "refused";
}

/// <summary>
/// The typed <c>details</c> an event may carry. Only these records are accepted, so a
/// password, token, setup code or session id cannot end up in an event by accident
/// (data-model.md: "never passwords, tokens, setup codes or session identifiers").
/// </summary>
internal abstract record SecurityEventDetails;

internal sealed record ReasonDetails(string Reason) : SecurityEventDetails;

internal sealed record SignInDetails(string? Reason, bool? KnownDevice) : SecurityEventDetails;

internal sealed record ClientBlockedDetails(DateTimeOffset Until) : SecurityEventDetails;

internal sealed record SessionsRevokedDetails(string Scope, string By) : SecurityEventDetails;

internal sealed record EmailChangedDetails(string OldEmail, string NewEmail) : SecurityEventDetails;

internal sealed record GrantSummary(string Role, Guid? SiteId);

internal sealed record AccountCreatedDetails(string Email, IReadOnlyList<GrantSummary> Grants) : SecurityEventDetails;

internal sealed record GrantsChangedDetails(IReadOnlyList<GrantSummary> Before, IReadOnlyList<GrantSummary> After) : SecurityEventDetails;

internal sealed record LinkIssuedDetails(DateTimeOffset ExpiresAt, string IssuedBy) : SecurityEventDetails;

internal sealed record OperatorCommandDetails(string Command) : SecurityEventDetails;

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
