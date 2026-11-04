using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Contracts.Events;

// Site export and import (020 research ST-19). Identity records each one as a security event.
// They carry ids, counts and codes only, never a site's name, a title or any content
// (Principle IX). The event type names are published contracts: renaming one is a breaking
// change.

/// <summary>An export of a site ended, with any outcome.</summary>
/// <param name="Outcome"><c>succeeded</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="Trigger"><c>admin</c>, <c>cli</c> or <c>pre_restore</c>.</param>
/// <param name="ActorUserId">Null for the operator command.</param>
public sealed record SiteExportFinished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid TransferId,
    Guid SiteId,
    string Outcome,
    string Trigger,
    Guid? ActorUserId,
    string? FailureCode) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteExportFinished";
}

/// <summary>An import into a site ended, with any outcome. <paramref name="SiteId"/> is null when a new site was never committed.</summary>
/// <param name="Mode"><c>new_site</c>, <c>merge</c> or <c>restore</c>.</param>
public sealed record SiteImportFinished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid TransferId,
    Guid? SiteId,
    string Mode,
    string Outcome,
    string Trigger,
    Guid? ActorUserId,
    string? FailureCode) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteImportFinished";
}

/// <summary>A finished export's archive was downloaded from the admin.</summary>
public sealed record SiteArchiveDownloaded(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid TransferId,
    Guid SiteId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteArchiveDownloaded";
}

/// <summary>A stored archive was deleted, by an Administrator or by retention (<paramref name="ActorUserId"/> null).</summary>
public sealed record SiteArchiveDeleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid TransferId,
    Guid? SiteId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteArchiveDeleted";
}
