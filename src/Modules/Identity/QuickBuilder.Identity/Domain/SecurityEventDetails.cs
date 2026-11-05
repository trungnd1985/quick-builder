namespace QuickBuilder.Identity.Domain.Details;

// The payloads a security event may carry in its jsonb details column. They are values
// serialised into one column, not stored rows, which is why they live apart from the
// entities in QuickBuilder.Identity.Domain: the architecture rule that every entity with a
// SiteId is site-owned (or reasoned [InstallationScoped]) applies to rows, and GrantSummary
// is not one.

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
/// 009 FR-028: which site, and which host name where one changed. The host name is what an
/// auditor needs; it lives here, in the record, and never in a log line (FR-032).
/// </summary>
internal sealed record SiteEventDetails(Guid SiteId, string? HostName) : SecurityEventDetails;

/// <summary>
/// 020 research ST-19: a site export, import or archive action. Ids and codes only: never a
/// site name, title or any content (Principle IX). <paramref name="Operator"/> marks the
/// operator command, which has no member.
/// </summary>
internal sealed record TransferEventDetails(Guid TransferId, Guid? SiteId, string? Mode, string? Trigger, string? FailureCode, bool Operator) : SecurityEventDetails;
