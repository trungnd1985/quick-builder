using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain.Menus;

/// <summary>
/// A named menu of a site, which Navigation blocks show (024 FR-001, FR-002, research NV-02).
/// </summary>
/// <remarks>
/// Its entries live in its <see cref="MenuDraft"/> and, once published, in a published version,
/// as a component's content does. The name lives here, so a rename never marks the draft as
/// changed; visitors see the name a version was published with (NV-05). Menus raise no
/// integration events: nothing outside Pages consumes them (data-model §6).
/// </remarks>
internal sealed class Menu : ISiteOwned
{
    public const int MaxNameLength = 100;

    /// <summary>The most menus one site may hold (data-model §1.1, research NV-20).</summary>
    public const int MaxPerSite = 50;

    private Menu(Guid id, Guid siteId, string name, Guid actor, DateTimeOffset now)
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
    private Menu()
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

    /// <param name="existingCount">How many menus the site has now, read in the create transaction.</param>
    public static Result<Menu> Create(Guid siteId, string? name, int existingCount, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (NormaliseName(name) is not { } cleanName)
        {
            return Result.Failure<Menu>(PagesErrors.InvalidMenuName);
        }

        if (existingCount >= MaxPerSite)
        {
            return Result.Failure<Menu>(PagesErrors.MenuLimitReached);
        }

        return Result.Success(new Menu(Guid.CreateVersion7(), siteId, cleanName, actor, clock.GetUtcNow()));
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
            return Result.Failure(PagesErrors.InvalidMenuName);
        }

        Name = cleanName;
        UpdatedAtUtc = clock.GetUtcNow();
        UpdatedBy = actor;
        return Result.Success();
    }
}
