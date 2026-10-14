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
/// simpler handlers elsewhere until complexity justifies more. Since 009 the site holds
/// real invariants — exactly one primary host name that is one of its own, at least one
/// host name, a supported language — so they live here, in one place, rather than being
/// re-checked by every caller.
/// </para>
/// <para>
/// <b>Concurrency.</b> <see cref="Version"/> is PostgreSQL's <c>xmin</c>, and every change
/// moves <see cref="UpdatedAtUtc"/>, so every change to the aggregate — including adding
/// or removing a host name — updates this row and is checked against the version it was
/// loaded with (009 research S-05). Two concurrent changes to one site cannot both commit,
/// which is what stops a race from removing the primary or the last host name.
/// </para>
/// </remarks>
internal sealed class Site
{
    public const int MaxNameLength = 200;

    private readonly List<SiteHostName> _hostNames = [];

    private Site(Guid id, string name, string scheme, string defaultLanguage, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Scheme = scheme;
        DefaultLanguage = defaultLanguage;
        Status = SiteStatus.Enabled;
        PrimaryHostName = string.Empty;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // EF Core materialisation.
    private Site()
    {
        Name = string.Empty;
        Scheme = SiteScheme.Https;
        DefaultLanguage = SupportedLanguages.Default;
        Status = SiteStatus.Enabled;
        PrimaryHostName = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary><c>https</c> or <c>http</c>. Shapes the base URL only.</summary>
    public string Scheme { get; private set; }

    /// <summary>A tag from <see cref="SupportedLanguages"/>, in canonical case.</summary>
    public string DefaultLanguage { get; private set; }

    /// <summary>A <see cref="SiteStatus"/> value.</summary>
    public string Status { get; private set; }

    /// <summary>
    /// The normalised name of the host name that forms the base URL. Always one of
    /// <see cref="HostNames"/>.
    /// </summary>
    /// <remarks>
    /// A column on the site rather than a flag on each host name: one column cannot hold
    /// two primaries, and changing the primary is one update rather than a swap between
    /// two rows that a unique index would check row by row (see data-model.md).
    /// </remarks>
    public string PrimaryHostName { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>The optimistic concurrency token, mapped to <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public bool IsEnabled => Status == SiteStatus.Enabled;

    /// <summary>Appended to every page's document title, e.g. <c>" | Acme"</c> (017 FR-005, FR-006).</summary>
    public string? SearchTitleSuffix { get; private set; }

    /// <summary>The media file used when a page has no sharing image (017 FR-005). Not checked here (research SE-08).</summary>
    public Guid? SearchDefaultImage { get; private set; }

    /// <summary>The organisation described on the home page (017 FR-034).</summary>
    public string? SearchOrganisationName { get; private set; }

    public Guid? SearchOrganisationLogo { get; private set; }

    /// <summary>"Discourage search engines" (017 FR-027).</summary>
    public bool SearchDiscouraged { get; private set; }

    /// <summary>The Administrator's own robots.txt rules, with <c>\n</c> line ends (017 FR-025).</summary>
    public string? SearchRobotsRules { get; private set; }

    /// <summary>"Redirect other host names to the primary one" (017 FR-014). On for sites created since 017.</summary>
    public bool RedirectOtherHosts { get; private set; }

    /// <summary>Who last changed the search settings or search visibility (017 FR-021); <see cref="UpdatedAtUtc"/> says when.</summary>
    public Guid? SearchUpdatedBy { get; private set; }

    public IReadOnlyCollection<SiteHostName> HostNames => _hostNames;

    public static Result<Site> Create(string name, IEnumerable<string> hostNames, TimeProvider clock) =>
        Create(name, hostNames, primaryHostName: null, scheme: null, defaultLanguage: null, clock);

    /// <summary>
    /// Creates an enabled site. The primary host name defaults to the first, the scheme to
    /// HTTPS and the language to English.
    /// </summary>
    public static Result<Site> Create(
        string name,
        IEnumerable<string> hostNames,
        string? primaryHostName,
        string? scheme,
        string? defaultLanguage,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(hostNames);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = TrimName(name);
        if (trimmedName is null)
        {
            return Result.Failure<Site>(InvalidName());
        }

        var parsedScheme = SiteScheme.Https;
        if (scheme is not null && !SiteScheme.TryParse(scheme, out parsedScheme))
        {
            return Result.Failure<Site>(InvalidScheme());
        }

        var language = SupportedLanguages.Default;
        if (defaultLanguage is not null && !SupportedLanguages.TryCanonicalise(defaultLanguage, out language))
        {
            return Result.Failure<Site>(UnsupportedLanguage());
        }

        var site = new Site(Guid.CreateVersion7(), trimmedName, parsedScheme, language, clock.GetUtcNow())
        {
            // 017 research SE-07: new sites answer on their primary host name only; sites
            // created before 017 keep the column's default, off.
            RedirectOtherHosts = true,
        };

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

        if (primaryHostName is null)
        {
            site.PrimaryHostName = site._hostNames[0].HostName;
        }
        else if (site.FindHostName(primaryHostName) is { } primary)
        {
            site.PrimaryHostName = primary.HostName;
        }
        else
        {
            return Result.Failure<Site>(InvalidPrimary());
        }

        return Result.Success(site);
    }

    /// <summary>
    /// Replaces every editable setting at once (009 research S-06). Nothing changes unless
    /// every value is valid.
    /// </summary>
    public Result ChangeSettings(
        string name,
        string scheme,
        string defaultLanguage,
        string primaryHostName,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = TrimName(name);
        if (trimmedName is null)
        {
            return Result.Failure(InvalidName());
        }

        if (!SiteScheme.TryParse(scheme, out var parsedScheme))
        {
            return Result.Failure(InvalidScheme());
        }

        if (!SupportedLanguages.TryCanonicalise(defaultLanguage, out var language))
        {
            return Result.Failure(UnsupportedLanguage());
        }

        if (FindHostName(primaryHostName) is not { } primary)
        {
            return Result.Failure(InvalidPrimary());
        }

        Name = trimmedName;
        Scheme = parsedScheme;
        DefaultLanguage = language;
        PrimaryHostName = primary.HostName;
        Touch(clock);
        return Result.Success();
    }

    /// <summary>
    /// Replaces the title suffix, the default sharing image and the organisation (017 FR-006,
    /// research SE-08). Nothing changes unless every value is valid.
    /// </summary>
    public Result ChangeSearchSettings(
        string? titleSuffix,
        Guid? defaultImageId,
        string? organisationName,
        Guid? organisationLogoId,
        Guid actorUserId,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var suffix = titleSuffix;
        if (suffix is { Length: > SiteSearchRules.MaxTitleSuffixLength } || SiteSearchRules.HasControlCharacters(suffix))
        {
            return Result.Failure(SiteSearchRules.Invalid(
                $"The title suffix must be at most {SiteSearchRules.MaxTitleSuffixLength} characters on one line."));
        }

        var organisation = organisationName?.Trim();
        if (organisation is { Length: > SiteSearchRules.MaxOrganisationNameLength } || SiteSearchRules.HasControlCharacters(organisation))
        {
            return Result.Failure(SiteSearchRules.Invalid(
                $"The organisation name must be at most {SiteSearchRules.MaxOrganisationNameLength} characters on one line."));
        }

        // The suffix keeps its leading space (" | Acme"); an empty one means none.
        SearchTitleSuffix = string.IsNullOrWhiteSpace(suffix) ? null : suffix;
        SearchDefaultImage = defaultImageId;
        SearchOrganisationName = string.IsNullOrEmpty(organisation) ? null : organisation;
        SearchOrganisationLogo = organisationLogoId;
        SearchUpdatedBy = actorUserId;
        Touch(clock);
        return Result.Success();
    }

    /// <summary>
    /// Replaces "Discourage search engines", the custom robots.txt rules and the host name
    /// redirect (017 FR-014, FR-025, FR-027). Nothing changes unless every value is valid.
    /// </summary>
    public Result ChangeSearchVisibility(
        bool discourage,
        string? robotsRules,
        bool redirectOtherHosts,
        Guid actorUserId,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (SiteSearchRules.NormaliseRobotsRules(robotsRules) is not { } rules)
        {
            return Result.Failure(SiteSearchRules.Invalid(
                $"Robots rules may be at most {SiteSearchRules.MaxRobotsRulesBytes / 1024} KB, with lines of at most {SiteSearchRules.MaxRobotsLineLength} characters and no control characters."));
        }

        SearchDiscouraged = discourage;
        SearchRobotsRules = rules.Length == 0 ? null : rules;
        RedirectOtherHosts = redirectOtherHosts;
        SearchUpdatedBy = actorUserId;
        Touch(clock);
        return Result.Success();
    }

    /// <summary>Adds a host name. It never becomes primary on its own.</summary>
    public Result AddHostName(string hostName, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!Shared.Kernel.MultiSite.HostNames.TryNormalise(hostName, out var normalised))
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
        Touch(clock);
        return Result.Success();
    }

    /// <summary>
    /// Removes a host name that is neither the primary nor the only one.
    /// </summary>
    public Result RemoveHostName(string hostName, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (FindHostName(hostName) is not { } existing)
        {
            return Result.Failure(Error.NotFound(
                "sites.host_name_not_found",
                "This site does not have that host name."));
        }

        // Checked before the primary rule: the only host name is always the primary, and
        // "make another one primary first" is impossible advice when there is no other.
        if (_hostNames.Count == 1)
        {
            return Result.Failure(Error.Conflict(
                "sites.last_host_name",
                "A site keeps at least one host name."));
        }

        if (string.Equals(existing.HostName, PrimaryHostName, StringComparison.Ordinal))
        {
            return Result.Failure(Error.Conflict(
                "sites.primary_host_name_removal",
                "Make another host name primary before removing this one."));
        }

        _hostNames.Remove(existing);
        Touch(clock);
        return Result.Success();
    }

    /// <summary>Takes the site off the public surface. True when the status changed.</summary>
    public bool Disable(TimeProvider clock) => ChangeStatus(SiteStatus.Disabled, clock);

    /// <summary>Puts the site back on the public surface. True when the status changed.</summary>
    public bool Enable(TimeProvider clock) => ChangeStatus(SiteStatus.Enabled, clock);

    private bool ChangeStatus(string status, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == status)
        {
            return false;
        }

        Status = status;
        Touch(clock);
        return true;
    }

    private SiteHostName? FindHostName(string? hostName) =>
        Shared.Kernel.MultiSite.HostNames.TryNormalise(hostName, out var normalised)
            ? _hostNames.Find(h => string.Equals(h.HostName, normalised, StringComparison.Ordinal))
            : null;

    private void Touch(TimeProvider clock) => UpdatedAtUtc = clock.GetUtcNow();

    private static string? TrimName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength ? null : trimmed;
    }

    private static Error InvalidName() => Error.Validation(
        "sites.invalid_name",
        $"A site name must be between 1 and {MaxNameLength} characters.");

    private static Error InvalidScheme() => Error.Validation(
        "sites.invalid_scheme",
        "The scheme must be https or http.");

    private static Error UnsupportedLanguage() => Error.Validation(
        "sites.unsupported_language",
        "That language is not on the supported list.");

    private static Error InvalidPrimary() => Error.Validation(
        "sites.primary_host_name_invalid",
        "The primary host name must be one of the site's host names.");
}
