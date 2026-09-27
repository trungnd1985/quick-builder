using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Isolation;

/// <summary>
/// How a module that stores site-owned records joins the shared isolation suite (009
/// FR-025, contracts/site-context.md §6).
/// </summary>
/// <remarks>
/// A probe works through the module's HTTP API only, so the suite tests what a caller can
/// actually reach, including the not-found rules, rather than what a DbContext would
/// return. <see cref="IsolationCoverageTests"/> fails the build when a module with an
/// <c>ISiteOwned</c> entity has no probe, or when an endpoint taking a record id is not
/// named in some probe's <see cref="CoveredEndpoints"/>.
/// </remarks>
public interface ISiteIsolationProbe
{
    /// <summary>The module's name, as its <c>IModuleDatabaseProvider</c> describes it.</summary>
    string Module { get; }

    /// <summary>
    /// Every endpoint this probe exercises with another site's record ids, as
    /// <c>METHOD pattern</c> exactly as routing reports it, e.g.
    /// <c>DELETE api/v1/media/sites/{siteId:guid}/files/{fileId:guid}</c>.
    /// </summary>
    IReadOnlyList<string> CoveredEndpoints { get; }

    /// <summary>A public path with the right shape that was never issued, for comparing misses.</summary>
    string? NeverIssuedPublicPath { get; }

    /// <summary>
    /// Creates at least one of each site-owned record on <paramref name="siteId"/> through the
    /// module's API, and says how to address them.
    /// </summary>
    Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName);

    /// <summary>
    /// Administrative requests naming <paramref name="siteId"/> but carrying the other site's
    /// record ids: reads, changes and deletes that must answer 404, and lists that must not
    /// contain them.
    /// </summary>
    IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other);

    /// <summary>Whether the seeded records are all still there.</summary>
    Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded);
}

/// <summary>What a probe seeded: the ids that address records, and their public paths.</summary>
public sealed record SeededRecords(IReadOnlyList<string> Ids, IReadOnlyList<string> PublicPaths);

/// <summary>One administrative request the suite sends.</summary>
/// <remarks>
/// <see cref="Body"/>, when set, is sent as JSON: an endpoint that takes a body refuses a
/// bodiless request before it ever looks for the record, which would prove nothing (010).
/// </remarks>
public sealed record ProbeRequest(HttpMethod Method, string Path, ProbeKind Kind, object? Body = null);

public enum ProbeKind
{
    /// <summary>Addresses one record by id: must answer 404 for another site's record.</summary>
    Addressed,

    /// <summary>Lists or shows: must answer 200 without any of another site's ids.</summary>
    Listing,
}
