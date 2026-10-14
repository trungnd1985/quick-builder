namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// What a redirect would do when followed, against the site's other redirects (017 FR-017,
/// FR-033; research SE-09).
/// </summary>
/// <remarks>
/// Visitors are redirected once per request, so a chain means a browser following several
/// hops, and a loop means one that never ends. A new or changed redirect is therefore walked
/// as a browser would walk it: its target's address, then whatever answers that address,
/// stopping at live content (which wins over redirects), at an absolute address, or at an
/// address nothing answers. Exact hops at the start of the walk are folded into the stored
/// target, so a chain is stored as one hop; a hop through a prefix is followed only to find
/// loops. More than ten hops is treated as a loop.
/// </remarks>
internal static class RedirectRules
{
    public const int MaxHops = 10;

    public static ChainCheck CheckChain(
        Guid? self, string matchKind, string sourcePath, string sourceQuery, RedirectTarget target, IRedirectGraph graph)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(graph);

        var final = target;
        var shortened = false;
        var foldable = true;
        Guid? lastHop = null;
        var current = graph.AddressOf(target);

        for (var hop = 0; current is not null; hop++)
        {
            if (LeadsBack(matchKind, sourcePath, sourceQuery, current))
            {
                return ChainCheck.Loop(lastHop);
            }

            if (graph.IsLiveContent(current) || graph.Follow(current, self) is not { } next)
            {
                break;
            }

            if (hop >= MaxHops)
            {
                return ChainCheck.Loop(next.RedirectId);
            }

            lastHop = next.RedirectId;
            if (foldable && next.IsExact)
            {
                final = next.Target;
                shortened = true;
            }
            else
            {
                foldable = false;
            }

            current = graph.AddressOf(next.Target) is { } address ? Join(address, next.Remainder) : null;
        }

        return new ChainCheck(IsLoop: false, LoopWith: null, final, shortened);
    }

    /// <summary>Whether a request for <paramref name="address"/> would meet the redirect being checked.</summary>
    private static bool LeadsBack(string matchKind, string sourcePath, string sourceQuery, string address)
    {
        if (matchKind == RedirectMatchKinds.Prefix)
        {
            return address == sourcePath || address.StartsWith(sourcePath + "/", StringComparison.Ordinal);
        }

        // An exact source with a query is met only by that query, which no walk carries.
        return sourceQuery.Length == 0 && address == sourcePath;
    }

    private static string Join(string address, string remainder) =>
        remainder.Length == 0 ? address : address.TrimEnd('/') + remainder;
}

/// <summary>The outcome of walking a redirect's chain.</summary>
/// <param name="IsLoop">Visitors would never arrive.</param>
/// <param name="LoopWith">The redirect that closes the loop, when one does.</param>
/// <param name="FinalTarget">The target to store: the original, or the end of its exact chain.</param>
/// <param name="Shortened">Whether <paramref name="FinalTarget"/> replaced the target given.</param>
internal sealed record ChainCheck(bool IsLoop, Guid? LoopWith, RedirectTarget? FinalTarget, bool Shortened)
{
    public static ChainCheck Loop(Guid? with) => new(IsLoop: true, with, FinalTarget: null, Shortened: false);
}

/// <summary>One redirect a walk goes through.</summary>
/// <param name="Remainder">For a prefix, the rest of the address after its source; empty for an exact one.</param>
internal sealed record RedirectHop(Guid RedirectId, RedirectTarget Target, string Remainder, bool IsExact);

/// <summary>A site's addresses and redirects, as a redirect check sees them.</summary>
internal interface IRedirectGraph
{
    /// <summary>
    /// The normalised address <paramref name="target"/> leads to now, without a query; null for
    /// an absolute address or a page or item with no address.
    /// </summary>
    string? AddressOf(RedirectTarget target);

    /// <summary>Whether a live page or item page answers <paramref name="path"/>, before any redirect.</summary>
    bool IsLiveContent(string path);

    /// <summary>
    /// The enabled redirect without a query that answers <paramref name="path"/>: the exact one,
    /// else the longest prefix; never <paramref name="except"/>.
    /// </summary>
    RedirectHop? Follow(string path, Guid? except);
}
