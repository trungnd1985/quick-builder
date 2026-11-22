using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// <see cref="IPreviewLinks"/>: what a secret opens, checked on every request and never cached
/// (022 research PV-08).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Throttled address groups wait (PV-18).</item>
/// <item>One indexed read by the secret's hash.</item>
/// <item>The link must be active: not revoked, not expired, created after the operator's switch.</item>
/// <item>Its creator must still hold the target kind's edit capability on the site (clarification 2).</item>
/// <item>Last-opened is written at most once a minute, so a reviewer reloading costs little.</item>
/// </list>
/// Every refusal is the same <see cref="PreviewLinkResolution.NotAvailable"/>; only an unknown
/// secret counts as a failed attempt.
/// </remarks>
internal sealed class PreviewLinkResolver(
    EditorDbContext database,
    PreviewLinkFailures failures,
    IMemberAccess members,
    IOptionsMonitor<PreviewOptions> options,
    TimeProvider clock) : IPreviewLinks
{
    private static readonly TimeSpan OpenedResolution = TimeSpan.FromSeconds(60);

    public async Task<PreviewLinkResolution> ResolveAsync(string secret, IPAddress? client, CancellationToken cancellationToken = default)
    {
        if (await failures.RetryAfterAsync(client, cancellationToken).ConfigureAwait(false) is { } wait)
        {
            return new PreviewLinkResolution.Throttled(wait);
        }

        var link = PreviewSecrets.IsWellFormed(secret)
            ? await FindAsync(PreviewSecrets.Hash(secret), cancellationToken).ConfigureAwait(false)
            : null;
        if (link is null)
        {
            await failures.CountAsync(client, cancellationToken).ConfigureAwait(false);
            return new PreviewLinkResolution.NotAvailable();
        }

        var now = clock.GetUtcNow();
        if (!link.IsActive(now, options.CurrentValue.LinksValidFrom)
            || !await members.HoldsCapabilityAsync(link.CreatedBy, EditingAccess.EditCapabilityOf(link.DocumentKind), link.SiteId, cancellationToken)
                .ConfigureAwait(false))
        {
            return new PreviewLinkResolution.NotAvailable();
        }

        if (link.LastOpenedAtUtc is not { } opened || now - opened >= OpenedResolution)
        {
            var threshold = now - OpenedResolution;
            await database.PreviewLinks
                .AcrossAllSites("A preview link names its own site (022 PV-08).")
                .Where(l => l.Id == link.Id && (l.LastOpenedAtUtc == null || l.LastOpenedAtUtc <= threshold))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastOpenedAtUtc, now), cancellationToken)
                .ConfigureAwait(false);
        }

        return new PreviewLinkResolution.Found(
            link.Id, link.SiteId, link.DocumentKind, link.DocumentId, link.VersionNumber, link.SampleItemId, link.ExpiresAtUtc);
    }

    /// <summary>
    /// The link with this secret hash, on any site: a request for <c>/_preview/l/…</c> has no site
    /// until the link names it.
    /// </summary>
    private Task<PreviewLink?> FindAsync(byte[] hash, CancellationToken cancellationToken) =>
        database.PreviewLinks
            .AcrossAllSites("A preview link names its own site (022 PV-08).")
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.SecretHash == hash, cancellationToken);
}
