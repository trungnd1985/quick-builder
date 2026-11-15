using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// A shared component, such as a header or footer, that page drafts reference by id
/// (FR-024 to FR-028, research P-11).
/// </summary>
/// <remarks>
/// References use the id, never the name, so a rename touches no page. Whether it is in
/// use is not a counter here: it is the rows of <see cref="ComponentUsage"/>, whose foreign
/// key refuses the delete of a used component in the database, where no race can get past
/// it.
/// </remarks>
internal sealed partial class Component : AggregateRoot, ISiteOwned, ITrashable
{
    public const int MaxNameLength = 100;

    private Component(Guid id, Guid siteId, string name, Guid actor, DateTimeOffset now)
    {
        Id = id;
        SiteId = siteId;
        Name = name;
        CreatedAtUtc = now;
        CreatedBy = actor;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    private Component()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>Unique within the site, ignoring letter case (<c>citext</c>).</summary>
    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>, sent to clients as <c>version</c>.</summary>
    public uint Version { get; private set; }

    /// <summary>023 FR-021: the component this one is a copy of; null unless made by "save my version as a copy".</summary>
    public Guid? CopiedFromId { get; private set; }

    /// <summary>When the source version the member had loaded was saved.</summary>
    public DateTimeOffset? CopiedFromSavedAtUtc { get; private set; }

    public Guid? CopiedBy { get; private set; }

    public DateTimeOffset? CopiedAtUtc { get; private set; }

    /// <summary>The copy origin, when this is a copy.</summary>
    public CopyOrigin? CopyOrigin =>
        CopiedFromId is { } source && CopiedFromSavedAtUtc is { } saved && CopiedBy is { } by && CopiedAtUtc is { } at
            ? new CopyOrigin(source, saved, by, at)
            : null;

    /// <summary>Records that this component was made as a copy (023 FR-020, FR-021).</summary>
    public void MarkCopyOf(CopyOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        CopiedFromId = origin.SourceId;
        CopiedFromSavedAtUtc = origin.SourceSavedAtUtc;
        CopiedBy = origin.CopiedBy;
        CopiedAtUtc = origin.CopiedAtUtc;
    }

    /// <summary>When the component was moved to the trash; null while it is live (021 TR-02).</summary>
    public DateTimeOffset? TrashedAtUtc { get; private set; }

    public Guid? TrashedBy { get; private set; }

    /// <summary>Whether it had a live version when trashed (TR-07).</summary>
    public bool? TrashedWasLive { get; private set; }

    /// <summary>Whether a publish was scheduled when it was trashed (TR-07).</summary>
    public bool? TrashedHadSchedule { get; private set; }

    public bool IsTrashed => TrashedAtUtc is not null;

    /// <summary>
    /// Moves the component to the trash (021 FR-001). Raises the existing deleted event, whose
    /// consumers must react to trashing (TR-08).
    /// </summary>
    public void MoveToTrash(Guid actor, bool wasLive, bool hadSchedule, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        TrashedAtUtc = now;
        TrashedBy = actor;
        TrashedWasLive = wasLive;
        TrashedHadSchedule = hadSchedule;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
        Raise(new ComponentDeletedEvent(SiteId, Id, now));
    }

    /// <summary>Takes the component out of the trash, under <paramref name="name"/> when its own is taken (FR-017).</summary>
    public void Restore(string name, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        Name = name;
        TrashedAtUtc = null;
        TrashedBy = null;
        TrashedWasLive = null;
        TrashedHadSchedule = null;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
        Raise(new ComponentRestoredEvent(SiteId, Id, now));
    }

    public static Result<Component> Create(Guid siteId, string? name, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (NormaliseName(name) is not { } cleanName)
        {
            return Result.Failure<Component>(PagesErrors.InvalidComponentName);
        }

        var now = clock.GetUtcNow();
        var component = new Component(Guid.CreateVersion7(), siteId, cleanName, actor, now);
        component.Raise(new ComponentCreatedEvent(siteId, component.Id, now));
        return Result.Success(component);
    }

    /// <summary>The trimmed name, or null when it is empty or too long.</summary>
    public static string? NormaliseName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength ? null : trimmed;
    }

    public Result Rename(string? name, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (NormaliseName(name) is not { } cleanName)
        {
            return Result.Failure(PagesErrors.InvalidComponentName);
        }

        Name = cleanName;
        UpdatedAtUtc = clock.GetUtcNow();
        UpdatedBy = actor;
        return Result.Success();
    }

    /// <summary>Records the deletion. The caller removes the row; the database refuses it if the component is used.</summary>
    public void MarkDeleted(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Raise(new ComponentDeletedEvent(SiteId, Id, clock.GetUtcNow()));
    }
}

/// <summary>
/// "This page's draft references this component" (research P-11). Rewritten with every page
/// draft save, in the same transaction.
/// </summary>
/// <remarks>
/// Belongs to no aggregate: it is an index the draft save maintains. Its composite foreign
/// keys are what make two rules facts of the database rather than checks in code — a
/// reference to another site's component cannot be stored, and a used component cannot be
/// deleted (FR-026, FR-028).
/// </remarks>
internal sealed class ComponentUsage : ISiteOwned
{
    public ComponentUsage(Guid siteId, Guid pageId, Guid componentId)
    {
        SiteId = siteId;
        PageId = pageId;
        ComponentId = componentId;
    }

    public Guid SiteId { get; private set; }

    public Guid PageId { get; private set; }

    public Guid ComponentId { get; private set; }
}
