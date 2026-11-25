using QuickBuilder.Host.Routing;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain.Redirects;

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
        var uncovered = Uncovered(RoutePrefixes.ReservedPrefixes
            .Concat(RoutePrefixes.PublicReservedPrefixes)
            .Concat(RoutePrefixes.UnresolvedPublicPrefixes));

        Assert.True(
            uncovered.Count == 0,
            "These platform prefixes could be page addresses. Add them to PageAddresses.ReservedFirstSegments " +
            "and docs/contracts/page-schema.md §5: " + string.Join(", ", uncovered));
    }

    /// <summary>012 research PR-08: the block stylesheet's prefix is a platform prefix no page can take.</summary>
    [Fact]
    public void The_assets_prefix_is_reserved_and_unproducible()
    {
        Assert.Contains("/_assets", RoutePrefixes.PublicReservedPrefixes);
        Assert.Empty(Uncovered(["/_assets"]));
    }

    [Fact]
    public void The_guard_fires_for_an_unreserved_prefix()
    {
        Assert.Equal(["/preview"], Uncovered(["/preview", "/_media", "/api"]));
    }

    /// <summary>
    /// #61: a redirect from an address the platform answers first would be saved and never
    /// fire (017 FR-017), so every platform prefix is refused as a redirect source.
    /// </summary>
    [Fact]
    public void Every_platform_prefix_is_refused_as_a_redirect_source()
    {
        var accepted = RoutePrefixes.ReservedPrefixes
            .Concat(RoutePrefixes.PublicReservedPrefixes)
            .Concat(RoutePrefixes.UnresolvedPublicPrefixes)
            .SelectMany(prefix => new[] { prefix, prefix + "/anything" })
            .Where(path => !RedirectSource.IsPlatformPath(path))
            .ToList();

        Assert.True(
            accepted.Count == 0,
            "These platform addresses could be redirect sources that never fire. Refuse them in RedirectSource.IsPlatformPath: "
            + string.Join(", ", accepted));
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
