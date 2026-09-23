namespace QuickBuilder.Sites.Contracts;

/// <summary>
/// A site as other modules and the HTTP surface see it.
/// </summary>
/// <remarks>
/// This is the module's public shape (FR-005). The <c>Site</c> entity behind it is
/// internal and never leaves the module — that separation is what lets the storage change
/// without breaking callers.
/// </remarks>
public sealed record SiteDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> HostNames,
    DateTimeOffset CreatedAtUtc);

/// <summary>One host name mapped to one site.</summary>
public sealed record SiteHostNameDto(Guid Id, Guid SiteId, string HostName);

/// <summary>
/// How other modules read site information (FR-005).
/// </summary>
/// <remarks>
/// The only supported way for another module to learn about a site. Reading the
/// <c>sites</c> schema directly is forbidden by FR-022 and caught by the architecture
/// tests: a module that owns no table in that schema has no business querying it.
/// </remarks>
public interface ISiteDirectory
{
    /// <summary>Resolves a host name to its site, or null when none matches.</summary>
    Task<SiteDto?> FindByHostNameAsync(string hostName, CancellationToken cancellationToken = default);

    Task<SiteDto?> FindByIdAsync(Guid siteId, CancellationToken cancellationToken = default);
}
