using System.Text.Json;

namespace QuickBuilder.Editor.Contracts;

/// <summary>
/// Reads a site's design token set (research ED-14). For the renderer (#12); nothing in this
/// release consumes it outside the Editor module.
/// </summary>
public interface IDesignTokenReader
{
    /// <summary>
    /// The site's stored token set, or <see cref="DefaultDesignTokens"/> with version 0 when
    /// it has never saved one. Does not check that the site exists.
    /// </summary>
    Task<DesignTokenSetDto> ReadAsync(Guid siteId, CancellationToken cancellationToken = default);
}

/// <summary>A site's token set, as stored or as the defaults.</summary>
/// <param name="SiteId">The owning site.</param>
/// <param name="Document">The token set document (docs/contracts/design-tokens.md).</param>
/// <param name="Version">The concurrency version a save must carry; 0 until the first save.</param>
/// <param name="IsDefault">True while the site has never saved a token set.</param>
/// <param name="UpdatedAtUtc">When it was last saved; null for the defaults.</param>
/// <param name="UpdatedBy">Who last saved it; null for the defaults.</param>
public sealed record DesignTokenSetDto(
    Guid SiteId,
    JsonElement Document,
    uint Version,
    bool IsDefault,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedBy);
