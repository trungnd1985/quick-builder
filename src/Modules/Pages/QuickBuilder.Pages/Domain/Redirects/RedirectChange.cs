using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// One recorded action on a redirect: who did what, and when (017 FR-021, research SE-13a,
/// data-model §1.7).
/// </summary>
/// <remarks>
/// Ids, codes and times only: never the source, the target or the note (Principle IX). It has
/// no foreign key to <see cref="Redirect"/>, so it outlives a deleted redirect.
/// </remarks>
internal sealed class RedirectChange : ISiteOwned
{
    public RedirectChange(Guid siteId, Guid redirectId, string action, string origin, Guid? actorUserId, DateTimeOffset atUtc)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        RedirectId = redirectId;
        Action = action;
        Origin = origin;
        ActorUserId = actorUserId;
        AtUtc = atUtc;
    }

    // EF Core materialisation.
    private RedirectChange()
    {
        Action = string.Empty;
        Origin = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid RedirectId { get; private set; }

    /// <summary>One of <see cref="RedirectChangeActions"/>.</summary>
    public string Action { get; private set; }

    /// <summary>One of <see cref="RedirectOrigins"/>.</summary>
    public string Origin { get; private set; }

    /// <summary>Null when a schedule or an event acted for nobody in particular.</summary>
    public Guid? ActorUserId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }
}

/// <summary>What a <see cref="RedirectChange"/> records.</summary>
internal static class RedirectChangeActions
{
    public const string Created = "created";

    public const string Changed = "changed";

    public const string TurnedOn = "turned_on";

    public const string TurnedOff = "turned_off";

    public const string Deleted = "deleted";

    public const string Imported = "imported";

    public const string Offered = "offered";

    public const string Retargeted = "retargeted";

    public static IReadOnlyList<string> All { get; } = [Created, Changed, TurnedOn, TurnedOff, Deleted, Imported, Offered, Retargeted];
}
