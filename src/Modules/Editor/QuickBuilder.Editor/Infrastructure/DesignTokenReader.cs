using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// A site's stored token set, or the defaults when it has none (research ED-14).
/// </summary>
internal sealed class DesignTokenReader(EditorDbContext database) : IDesignTokenReader
{
    public async Task<DesignTokenSetDto> ReadAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var stored = await database.ForSite<DesignTokenSet>(siteId)
            .AsNoTracking()
            .Select(t => new { t.Document, t.Version, t.UpdatedAtUtc, t.UpdatedBy })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return new DesignTokenSetDto(siteId, DefaultDesignTokens.Document, 0, IsDefault: true, UpdatedAtUtc: null, UpdatedBy: null);
        }

        using var document = JsonDocument.Parse(stored.Document);
        return new DesignTokenSetDto(siteId, document.RootElement.Clone(), stored.Version, IsDefault: false, stored.UpdatedAtUtc, stored.UpdatedBy);
    }
}
