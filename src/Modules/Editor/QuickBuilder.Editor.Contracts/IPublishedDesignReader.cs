using System.Text.Json;

namespace QuickBuilder.Editor.Contracts;

/// <summary>
/// The design visitors get: a site's published token set (012 contracts/readers.md §1.2).
/// </summary>
/// <remarks>
/// The draft token set (<see cref="IDesignTokenReader"/>) never reaches visitors; only a
/// published copy does (FR-010). Before a site first publishes its design, visitors get the
/// built-in defaults as design number 0. The Editor module implements this over a read-only
/// connection (research PR-06).
/// </remarks>
public interface IPublishedDesignReader
{
    /// <summary>
    /// The live design number, or 0 while the site uses the defaults. One primary-key lookup,
    /// read on every public request, because rendered pages are cached by it (PR-05).
    /// </summary>
    Task<int> GetLiveNumberAsync(Guid siteId, CancellationToken cancellationToken = default);

    /// <summary>The live design, or <see cref="DefaultDesignTokens"/> as number 0.</summary>
    Task<PublishedDesign> GetLiveAsync(Guid siteId, CancellationToken cancellationToken = default);
}

/// <summary>A published token set.</summary>
/// <param name="Number">The design version number; 0 for the built-in defaults.</param>
/// <param name="SchemaVersion">The design-token contract version of <paramref name="Document"/>.</param>
/// <param name="Document">The token set (docs/contracts/design-tokens.md).</param>
public sealed record PublishedDesign(int Number, int SchemaVersion, JsonElement Document);
