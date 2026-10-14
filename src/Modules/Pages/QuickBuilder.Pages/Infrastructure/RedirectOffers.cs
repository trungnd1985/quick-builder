using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Features;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>One change to a site's redirects that applying offers decided on.</summary>
internal abstract record OfferDecision
{
    public sealed record Create(CheckedRedirect Redirect) : OfferDecision;

    public sealed record Retarget(Guid RedirectId, RedirectTarget Target) : OfferDecision;

    public sealed record TurnOff(Guid RedirectId) : OfferDecision;
}

/// <summary>
/// Applies offered redirects to a site's redirects, in the caller's transaction (017 research
/// SE-11; data-model §3.4).
/// </summary>
/// <remarks>
/// In order, for each offer:
/// <list type="number">
/// <item>an exact source that live content now holds is skipped (FR-031);</item>
/// <item>redirects whose <c>path</c> target is the old address follow it to the new one;</item>
/// <item>redirects the new one would loop with are turned off, among them a template's earlier
/// prefix when it moves back;</item>
/// <item>a source that already has a redirect has that redirect retargeted; otherwise a
/// redirect is added (origin <c>offer</c>).</item>
/// </list>
/// Every action is recorded in <c>pages.redirect_changes</c> (SE-13a) by <see cref="RedirectWrites"/>.
/// </remarks>
internal sealed class RedirectOffers(PagesDbContext database, RedirectWrites writes, TimeProvider clock)
{
    /// <summary>The most turned-off redirects one offer may need before it is skipped instead.</summary>
    private const int MaxTurnOffs = 10;

    /// <summary>
    /// What applying <paramref name="offers"/> to <paramref name="state"/> changes, and the counts
    /// for the outcome. Pure: <paramref name="state"/> is updated as the decisions are made, so
    /// later offers see earlier ones.
    /// </summary>
    public static (IReadOnlyList<OfferDecision> Decisions, int Skipped) Decide(RedirectSiteState state, IReadOnlyList<OfferedRedirect> offers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(offers);

        var decisions = new List<OfferDecision>();
        var skipped = 0;

        foreach (var offer in offers)
        {
            if (offer.Match == RedirectMatchKinds.Exact && state.IsLiveContent(offer.SourcePath))
            {
                skipped++;
                continue;
            }

            TurnOffReversed(state, offer, decisions);
            Retarget(state, offer, decisions);

            var taken = state.SourceTakenBy(offer.Match, offer.SourcePath, string.Empty);
            var checkedRedirect = CheckTurningOffLoops(state, taken, offer, decisions);
            if (checkedRedirect is null || (taken is null && !RedirectWrites.HasRoomFor(state, 1)))
            {
                skipped++;
                continue;
            }

            if (taken is { } existing)
            {
                decisions.Add(new OfferDecision.Retarget(existing, checkedRedirect.Target));
                var entry = state.Find(existing)!;
                state.Add(entry with { Target = checkedRedirect.Target, Enabled = true });
            }
            else
            {
                decisions.Add(new OfferDecision.Create(checkedRedirect));
                state.Add(new RedirectEntry(Guid.NewGuid(), checkedRedirect.Match, checkedRedirect.Path, checkedRedirect.Query, checkedRedirect.Target, Enabled: true));
            }
        }

        return (decisions, skipped);
    }

    /// <summary>Applies the offers and returns the outcome; the caller saves.</summary>
    public async Task<RedirectOutcomeResponse> ApplyAsync(RedirectSiteState state, IReadOnlyList<OfferedRedirect> offers, Guid? actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(offers);

        if (offers.Count == 0)
        {
            return RedirectOutcomeResponse.None;
        }

        var (decisions, skipped) = Decide(state, offers);
        var changed = decisions.SelectMany(d => d switch
            {
                OfferDecision.Retarget r => [r.RedirectId],
                OfferDecision.TurnOff t => new[] { t.RedirectId },
                _ => [],
            })
            .Distinct()
            .ToList();

        var tracked = changed.Count == 0
            ? []
            : await database.ForSite<Redirect>(state.SiteId).Where(r => changed.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken).ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var created = 0;
        var retargeted = new HashSet<Guid>();
        var turnedOff = new List<Guid>();
        foreach (var decision in decisions)
        {
            switch (decision)
            {
                case OfferDecision.Create create:
                    writes.Add(state, create.Redirect, 301, enabled: true, note: null, RedirectOrigins.Offer, actor);
                    created++;
                    break;

                case OfferDecision.Retarget retarget when tracked.TryGetValue(retarget.RedirectId, out var redirect):
                    redirect.Retarget(retarget.Target, actor, now);
                    writes.Record(redirect, RedirectChangeActions.Retargeted, actor, now);
                    retargeted.Add(redirect.Id);
                    break;

                case OfferDecision.TurnOff turnOff when tracked.TryGetValue(turnOff.RedirectId, out var redirect) && redirect.Enabled:
                    redirect.TurnOff(actor, now);
                    writes.Record(redirect, RedirectChangeActions.TurnedOff, actor, now);
                    turnedOff.Add(redirect.Id);
                    break;
            }
        }

        return new RedirectOutcomeResponse(created, skipped, retargeted.Count, turnedOff);
    }

    /// <summary>
    /// The offer accepted when a page is unpublished or deleted (017 FR-032): its address to
    /// <paramref name="targetPageId"/>. A template has no address of its own, and a prefix keeps
    /// the rest of the path, so it offers nothing. Runs in the caller's transaction after the page
    /// stopped being live; the caller saves.
    /// </summary>
    public async Task<Shared.Kernel.Results.Result<RedirectOutcomeResponse>> RedirectRemovedPageAsync(
        Guid siteId, Guid pageId, Guid targetPageId, Guid actor, CancellationToken cancellationToken)
    {
        var state = await writes.LoadAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (CheckRemovedTarget(state, pageId, targetPageId) is { } refused)
        {
            return Shared.Kernel.Results.Result.Failure<RedirectOutcomeResponse>(refused);
        }

        var page = state.Pages[pageId];
        return page.IsTemplate
            ? Shared.Kernel.Results.Result.Success(RedirectOutcomeResponse.None)
            : Shared.Kernel.Results.Result.Success(await ApplyAsync(
                state, [new OfferedRedirect(RedirectMatchKinds.Exact, page.Path, RedirectTarget.ToPage(targetPageId))], actor, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Why a removed page cannot redirect to <paramref name="targetPageId"/>, or null when it can.</summary>
    public static Shared.Kernel.Results.Error? CheckRemovedTarget(RedirectSiteState state, Guid pageId, Guid targetPageId)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Pages.ContainsKey(pageId))
        {
            return PagesErrors.NotFound;
        }

        return targetPageId != pageId && state.Pages.TryGetValue(targetPageId, out var target) && !target.IsTemplate
            ? null
            : PagesErrors.RedirectTargetInvalid("choose another page of this site");
    }

    /// <summary>
    /// Redirects sending visitors to the old address by path now send them to the new one; one that
    /// would then send visitors to its own source is turned off instead.
    /// </summary>
    private static void Retarget(RedirectSiteState state, OfferedRedirect offer, List<OfferDecision> decisions)
    {
        foreach (var entry in state.Entries().Where(e => e.Enabled && e.Target.Kind == RedirectTargetKinds.Path).ToList())
        {
            if (state.AddressOf(entry.Target) is not { } address)
            {
                continue;
            }

            RedirectTarget? next = offer.Match switch
            {
                RedirectMatchKinds.Exact when address == offer.SourcePath => offer.Target,
                RedirectMatchKinds.Prefix when address == offer.SourcePath || address.StartsWith(offer.SourcePath + "/", StringComparison.Ordinal) =>
                    state.AddressOf(offer.Target) is { } to ? RedirectTarget.ToPath(to + address[offer.SourcePath.Length..]) : null,
                _ => null,
            };

            if (next is null || next == entry.Target)
            {
                continue;
            }

            if (state.AddressOf(next) is { } nextAddress
                && (nextAddress == entry.Path
                    || (entry.Match == RedirectMatchKinds.Prefix && nextAddress.StartsWith(entry.Path + "/", StringComparison.Ordinal))))
            {
                TurnOff(state, entry, decisions);
            }
            else
            {
                decisions.Add(new OfferDecision.Retarget(entry.Id, next));
                state.Add(entry with { Target = next });
            }
        }
    }

    /// <summary>
    /// A template moving back: the earlier prefix from the new place to the old one would send
    /// the items in a circle, so it is turned off.
    /// </summary>
    private static void TurnOffReversed(RedirectSiteState state, OfferedRedirect offer, List<OfferDecision> decisions)
    {
        if (offer.Match != RedirectMatchKinds.Prefix || state.AddressOf(offer.Target) is not { } to)
        {
            return;
        }

        foreach (var entry in state.Entries().Where(e => e.Enabled && e.Match == RedirectMatchKinds.Prefix && e.Path == to).ToList())
        {
            if (state.AddressOf(entry.Target) is { } back
                && (back == offer.SourcePath || back.StartsWith(offer.SourcePath + "/", StringComparison.Ordinal)))
            {
                TurnOff(state, entry, decisions);
            }
        }
    }

    /// <summary>The offer checked, turning off each redirect it would loop with; null when it cannot be added.</summary>
    private static CheckedRedirect? CheckTurningOffLoops(RedirectSiteState state, Guid? self, OfferedRedirect offer, List<OfferDecision> decisions)
    {
        for (var attempt = 0; attempt <= MaxTurnOffs; attempt++)
        {
            var result = RedirectWrites.Check(state, self, offer.Match, offer.SourcePath, offer.Target, 301, note: null);
            if (result.IsSuccess)
            {
                return result.Value;
            }

            if (result.Error.Code != LoopCode
                || LoopWith(result.Error) is not { } loopWith
                || state.Find(loopWith) is not { Enabled: true } entry)
            {
                return null;
            }

            TurnOff(state, entry, decisions);
        }

        return null;
    }

    private static void TurnOff(RedirectSiteState state, RedirectEntry entry, List<OfferDecision> decisions)
    {
        decisions.Add(new OfferDecision.TurnOff(entry.Id));
        state.Add(entry with { Enabled = false });
    }

    private const string LoopCode = "redirects.loop";

    private static Guid? LoopWith(Shared.Kernel.Results.Error error) =>
        PagesErrors.ExtensionsOf(error) is { } fields && fields.TryGetValue("redirectId", out var id) && id is Guid redirectId ? redirectId : null;
}
