using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain.Menus;

/// <summary>
/// "This menu's draft points at that page or item" (024 data-model §1.3, research NV-04).
/// </summary>
/// <remarks>
/// An index the draft save rewrites in its transaction. It tells the save which targets are
/// new (rule 8 checks only those), and answers "which menus point at this?" for the warnings of
/// FR-026, together with the live version's dependencies.
/// </remarks>
internal sealed class MenuTarget : ISiteOwned
{
    public MenuTarget(Guid siteId, Guid menuId, string targetKind, Guid targetId)
    {
        SiteId = siteId;
        MenuId = menuId;
        TargetKind = targetKind;
        TargetId = targetId;
    }

    public Guid SiteId { get; private set; }

    public Guid MenuId { get; private set; }

    /// <summary><see cref="MenuTargetKinds.Page"/> or <see cref="MenuTargetKinds.Item"/>.</summary>
    public string TargetKind { get; private set; }

    public Guid TargetId { get; private set; }
}

/// <summary>The kinds of target <see cref="MenuTarget"/> records.</summary>
internal static class MenuTargetKinds
{
    public const string Page = "page";

    public const string Item = "item";
}

/// <summary>
/// "This page's or component's draft shows that menu" (024 data-model §1.4, research NV-06).
/// </summary>
/// <remarks>
/// Rewritten with every page and component draft save, beside <see cref="ComponentUsage"/>,
/// but without a restricting key: deleting a menu that only drafts show is allowed (FR-003),
/// and removes these rows with it, leaving the drafts' <c>menuId</c> dangling.
/// </remarks>
internal sealed class MenuUsage : ISiteOwned
{
    public MenuUsage(Guid siteId, string subjectKind, Guid subjectId, Guid menuId)
    {
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        MenuId = menuId;
    }

    public Guid SiteId { get; private set; }

    /// <summary><see cref="SubjectKinds.Page"/> or <see cref="SubjectKinds.Component"/>.</summary>
    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    public Guid MenuId { get; private set; }
}

/// <summary>
/// One change to a menu, for the audit of FR-005 (024 data-model §1.5).
/// </summary>
/// <remarks>
/// Ids and the action only: no name, label or address (constitution Principle IX). No foreign
/// key, so the record outlives the menu. Publishing actions are in the publish log.
/// </remarks>
internal sealed class MenuChange : ISiteOwned
{
    public MenuChange(Guid siteId, Guid menuId, string action, Guid actorUserId, DateTimeOffset atUtc)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        MenuId = menuId;
        Action = action;
        ActorUserId = actorUserId;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid MenuId { get; private set; }

    /// <summary>One of <see cref="MenuChangeActions"/>.</summary>
    public string Action { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }
}

/// <summary>What <see cref="MenuChange"/> records.</summary>
internal static class MenuChangeActions
{
    public const string Created = "created";

    public const string Renamed = "renamed";

    public const string Duplicated = "duplicated";

    public const string DraftSaved = "draft_saved";

    public const string Deleted = "deleted";

    public static IReadOnlyList<string> All { get; } = [Created, Renamed, Duplicated, DraftSaved, Deleted];
}
