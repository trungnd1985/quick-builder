using QuickBuilder.Host.Routing;
using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// No page can ever sit where the platform answers first (010 FR-007, research P-04).
/// </summary>
/// <remarks>
/// The Pages module cannot reference the host (constitution Principle I), so the reserved
/// list is its own. This keeps it complete: every prefix the host reserves is either on the
/// list or impossible to produce with the slug grammar. A new platform prefix fails the
/// build until Pages reserves it.
/// </remarks>
public sealed class ReservedAddressCoverageTests
{
    [Fact]
    public void Every_platform_prefix_is_reserved_or_unproducible()
    {
        var uncovered = Uncovered(RoutePrefixes.ReservedPrefixes.Concat(RoutePrefixes.PublicReservedPrefixes));

        Assert.True(
            uncovered.Count == 0,
            "These platform prefixes could be page addresses. Add them to PageAddresses.ReservedFirstSegments " +
            "and docs/contracts/page-schema.md §5: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void The_guard_fires_for_an_unreserved_prefix()
    {
        Assert.Equal(["/preview"], Uncovered(["/preview", "/_media", "/api"]));
    }

    private static List<string> Uncovered(IEnumerable<string> prefixes) =>
    [
        .. prefixes.Where(prefix =>
        {
            var segment = prefix.TrimStart('/');
            return PageAddresses.IsValidSlug(segment) && !PageAddresses.IsReservedFirstSegment(segment);
        }),
    ];
}
