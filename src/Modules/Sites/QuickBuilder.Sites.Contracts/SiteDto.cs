namespace QuickBuilder.Sites.Contracts;

/// <summary>
/// A site as other modules and the HTTP surface see it.
/// </summary>
/// <remarks>
/// This is the module's public shape (FR-005). The <c>Site</c> entity behind it is
/// internal and never leaves the module — that separation is what lets the storage change
/// without breaking callers.
/// <para>
/// 009 added the settings as <c>init</c> properties rather than constructor parameters, so
/// the constructor other code may already use keeps its shape (research S-17). Only the
/// Sites module constructs this type.
/// </para>
/// </remarks>
public sealed record SiteDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> HostNames,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>The normalised host name the base URL is built from. First in <see cref="HostNames"/>.</summary>
    public required string PrimaryHostName { get; init; }

    /// <summary><c>https</c> or <c>http</c>.</summary>
    public required string Scheme { get; init; }

    /// <summary>
    /// <c>{scheme}://{primary host name}{Hosting:BasePath}</c>, derived every time and never
    /// stored (009 research S-07). Build absolute addresses from this, never from a request.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>A BCP 47 tag from the supported list, e.g. <c>en</c> or <c>pt-BR</c>.</summary>
    public required string DefaultLanguage { get; init; }

    /// <summary><c>enabled</c> or <c>disabled</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Opaque concurrency token. Send it back with a settings change.</summary>
    public required uint Version { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>One host name mapped to one site.</summary>
public sealed record SiteHostNameDto(Guid Id, Guid SiteId, string HostName);

/// <summary>
/// How other modules read site information (FR-005).
/// </summary>
/// <remarks>
/// The only supported way for another module to learn about a site. Reading the
/// <c>sites</c> schema directly is forbidden by FR-022 and caught by the architecture
/// tests: a module that owns no table in that schema has no business querying it.
/// <para>
/// Nothing here is cached across requests (009 research S-01): a change is visible on
/// every instance at the next request. Within one request, the resolved site is read once.
/// </para>
/// </remarks>
public interface ISiteDirectory
{
    /// <summary>
    /// The <b>enabled</b> site holding this host name, or null when none does.
    /// </summary>
    /// <remarks>
    /// A disabled site's host names answer null exactly as unknown ones do (009 research
    /// S-03). This exists for the host's site-resolution middleware, and an architecture
    /// test fails if anything else calls it.
    /// </remarks>
    Task<SiteDto?> FindByHostNameAsync(string hostName, CancellationToken cancellationToken = default);

    /// <summary>A site by id, in any status, or null.</summary>
    Task<SiteDto?> FindByIdAsync(Guid siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any site, in any status, holds this host name. False for a name that is not
    /// a valid host name.
    /// </summary>
    Task<bool> IsHostNameTakenAsync(string hostName, CancellationToken cancellationToken = default);
}
