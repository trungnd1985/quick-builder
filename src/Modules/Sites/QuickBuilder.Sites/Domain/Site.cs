using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Sites.Domain;

/// <summary>
/// A website hosted by this installation.
/// </summary>
/// <remarks>
/// Not <see cref="ISiteOwned"/>: a site is the scope, not something inside one. That is
/// why listing sites is a deliberate cross-site read (FR-013a) rather than an ordinary
/// filtered query.
/// <para>
/// The constitution requires a rich domain model for Pages, Cms and Rendering, and allows
/// simpler handlers elsewhere until complexity justifies more. Sites keeps just enough
/// behaviour here to hold its own invariants — name bounds and host name shape — so those
/// rules live in one place rather than being re-checked by every caller.
/// </para>
/// </remarks>
internal sealed class Site
{
    public const int MaxNameLength = 200;

    private readonly List<SiteHostName> _hostNames = [];

    private Site(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // EF Core materialisation.
    private Site()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<SiteHostName> HostNames => _hostNames;

    public static Result<Site> Create(string name, IEnumerable<string> hostNames, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(hostNames);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            return Result.Failure<Site>(Error.Validation(
                "sites.invalid_name",
                $"A site name must be between 1 and {MaxNameLength} characters."));
        }

        var site = new Site(Guid.CreateVersion7(), trimmed, clock.GetUtcNow());

        foreach (var hostName in hostNames)
        {
            var added = site.AddHostName(hostName, clock);
            if (added.IsFailure)
            {
                return Result.Failure<Site>(added.Error);
            }
        }

        if (site._hostNames.Count == 0)
        {
            return Result.Failure<Site>(Error.Validation(
                "sites.no_host_names",
                "A site must have at least one host name."));
        }

        return Result.Success(site);
    }

    public Result AddHostName(string hostName, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var normalised = SiteHostName.Normalise(hostName);
        if (!SiteHostName.IsValid(normalised))
        {
            return Result.Failure(Error.Validation(
                "sites.invalid_host_name",
                $"'{hostName}' is not a valid host name."));
        }

        // Within-site duplicates are caught here; across-site duplicates are caught by the
        // unique index, because only the database can see other sites' rows.
        if (_hostNames.Exists(h => string.Equals(h.HostName, normalised, StringComparison.Ordinal)))
        {
            return Result.Failure(Error.Conflict(
                "sites.host_name_taken",
                "The host name is already assigned to this site."));
        }

        _hostNames.Add(SiteHostName.Create(Id, normalised, clock.GetUtcNow()));
        UpdatedAtUtc = clock.GetUtcNow();
        return Result.Success();
    }
}
