using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// A site's stored token set, or the defaults when it has none (research ED-14), read at the
/// current schema version (019 FR-021).
/// </summary>
internal sealed class DesignTokenReader(EditorDbContext database, DesignTokenUpgrades upgrades) : IDesignTokenReader
{
    /// <summary>
    /// The token set for anyone who only needs to look: an older set is upgraded in memory; one
    /// that cannot be used comes back as stored. Editing and publishing use
    /// <see cref="ReadCurrentAsync"/>, which refuses it.
    /// </summary>
    public async Task<DesignTokenSetDto> ReadAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var stored = await StoredAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return Defaults(siteId);
        }

        var current = upgrades.ForEditing("design-tokens", siteId, stored.Document, stored.SchemaVersion);
        using var raw = JsonDocument.Parse(stored.Document);
        return Dto(siteId, stored, current.IsSuccess ? current.Value : raw.RootElement.Clone());
    }

    /// <summary>The token set at the current schema version, or why it cannot be edited or published.</summary>
    public async Task<Result<DesignTokenSetDto>> ReadCurrentAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var stored = await StoredAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return Result.Success(Defaults(siteId));
        }

        var current = upgrades.ForEditing("design-tokens", siteId, stored.Document, stored.SchemaVersion);
        return current.IsFailure
            ? Result.Failure<DesignTokenSetDto>(current.Error)
            : Result.Success(Dto(siteId, stored, current.Value));
    }

    private Task<Stored?> StoredAsync(Guid siteId, CancellationToken cancellationToken) =>
        database.ForSite<DesignTokenSet>(siteId)
            .AsNoTracking()
            .Select(t => new Stored(t.Document, t.SchemaVersion, t.Version, t.UpdatedAtUtc, t.UpdatedBy))
            .FirstOrDefaultAsync(cancellationToken);

    private static DesignTokenSetDto Defaults(Guid siteId) =>
        new(siteId, DefaultDesignTokens.Document, 0, IsDefault: true, UpdatedAtUtc: null, UpdatedBy: null);

    private static DesignTokenSetDto Dto(Guid siteId, Stored stored, JsonElement document) =>
        new(siteId, document, stored.Version, IsDefault: false, stored.UpdatedAtUtc, stored.UpdatedBy);

    private sealed record Stored(string Document, int SchemaVersion, uint Version, DateTimeOffset UpdatedAtUtc, Guid UpdatedBy);
}
