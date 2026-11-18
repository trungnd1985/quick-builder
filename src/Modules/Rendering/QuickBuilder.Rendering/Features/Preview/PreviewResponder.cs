using System.Text;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>The kinds of document a preview shows (022 data-model §1).</summary>
internal static class PreviewKinds
{
    public const string Page = "page";

    public const string Item = "item";
}

/// <summary>What a preview shows: a page or item, its draft or one version, and for a template page its sample item.</summary>
internal sealed record PreviewTarget(string Kind, Guid DocumentId, int? VersionNumber = null, Guid? SampleItemId = null);

/// <summary>
/// One preview request (022 research PV-05, PV-06).
/// </summary>
/// <param name="SelfPath">The preview's own address relative to the base path, for pager links.</param>
/// <param name="MediaPrefix">The preview's media route relative to the base path.</param>
/// <param name="ExpiresAt">For a link viewer, when the link stops working; null for members.</param>
internal sealed record PreviewRequest(
    SiteDto Site,
    PreviewTarget Target,
    PreviewMode Mode,
    string BasePath,
    string SelfPath,
    string MediaPrefix,
    IReadOnlyDictionary<string, string?>? Query,
    DateTimeOffset? ExpiresAt);

/// <summary>How a preview request ended.</summary>
internal abstract record PreviewOutcome
{
    /// <summary>The rendered preview, and the media files it references (PV-11).</summary>
    public sealed record Rendered(RenderedPage Page, IReadOnlySet<Guid> Media) : PreviewOutcome;

    /// <summary>The target is not readable: missing, trashed, of another site, or its version is gone.</summary>
    public sealed record NotFound : PreviewOutcome;

    /// <summary>The item's collection has no template page to show it in.</summary>
    public sealed record NoTemplate : PreviewOutcome;

    /// <summary>The page draft fails the page schema contract (PV-15).</summary>
    public sealed record InvalidDraft(string Code, string Rule, string Location, string Message) : PreviewOutcome;
}

/// <summary>
/// Renders a preview (022 research PV-03, PV-04, PV-13, PV-15): resolves the target through the
/// preview readers, composes it with <see cref="PageComposer"/> and renders it with
/// <see cref="PageResponder.Render"/>, the very steps a published page takes.
/// </summary>
/// <remarks>
/// It never touches the render cache and never builds a render key (spec FR-023): a preview is
/// rendered fresh every time and stored nowhere. It reads nothing but drafts, versions and the
/// live site, and writes nothing.
/// </remarks>
internal sealed class PreviewResponder(
    IPreviewContentReader pages,
    IPreviewItemReader items,
    IPublishedItemReader publishedItems,
    ITemplateReader templates,
    IPageDocumentValidator validator,
    IDesignTokenReader designTokens,
    IUserDirectory users,
    LiveContentSource live,
    PageComposer composer,
    PageRenderer renderer,
    ILogger<PreviewResponder> logger)
{
    /// <summary>The admin app's root, under the base path, for the member bar's links.</summary>
    private const string AdminRoot = "/admin";

    public async Task<PreviewOutcome> RespondAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = request.Target.Kind == PreviewKinds.Item
            ? await ResolveItemAsync(request, cancellationToken).ConfigureAwait(false)
            : await ResolvePageAsync(request, cancellationToken).ConfigureAwait(false);
        if (resolved is not Resolved target)
        {
            return resolved as PreviewOutcome ?? new PreviewOutcome.NotFound();
        }

        var site = request.Site;
        var basePath = PageShell.TrimBase(request.BasePath);
        var addresses = new PreviewAddresses(
            site.HostNames.Count > 0 ? site.BaseUrl : null,
            request.MediaPrefix,
            "/_preview/assets",
            request.SelfPath,
            "/_preview/form");
        var source = new PreviewContentSource(live, pages, designTokens, addresses);
        source.Record(target.TemplateFromDraft);
        source.Record(target.SampleFromDraft);

        // A preview is never cached, so every query parameter may name a list's page (016 TB-11).
        var listRequest = new ListRequest(target.Path, PageParams.From([.. request.Query?.Keys ?? []], request.Query));

        var inputs = await composer.ComposeAsync(source, site, target.PageId, target.Version, request.BasePath, target.Item, listRequest, cancellationToken)
            .ConfigureAwait(false);

        var by = target.By is { } author
            && await users.FindManyAsync([author], cancellationToken).ConfigureAwait(false) is [var person, ..]
                ? person.DisplayName
                : null;
        var bar = new PreviewBarModel(
            target.VersionNumber is null ? PreviewState.Draft : PreviewState.Version,
            target.At,
            by,
            target.VersionNumber,
            target.SampleItemName,
            source.FromDrafts,
            target.ScheduledAt,
            target.NeedsAttention,
            request.ExpiresAt,
            request.Mode == PreviewMode.Member ? basePath + target.EditAddress : null,
            request.Mode == PreviewMode.Member ? basePath + ShareAddress(target) : null);

        inputs = inputs with { Preview = new PreviewContext(request.Mode, bar, addresses) };
        var page = PageResponder.Render(inputs, renderer, context =>
        {
            if (context.BrokenBindings.Count > 0)
            {
                // 016 FR-038: node ids and reasons, never values.
                logger.LogInformation(
                    "A preview of {Kind} {DocumentId} on site {SiteId} rendered with {Count} broken binding(s).",
                    request.Target.Kind, request.Target.DocumentId, site.Id, context.BrokenBindings.Count);
            }
        });

        return new PreviewOutcome.Rendered(page, inputs.Media.Keys.ToHashSet());
    }

    private async Task<object> ResolvePageAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        var siteId = request.Site.Id;
        var target = request.Target;

        if (target.VersionNumber is { } number)
        {
            var version = await pages.GetPageVersionAsync(siteId, target.DocumentId, number, cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                return new PreviewOutcome.NotFound();
            }

            var (item, itemName, fromDraft, path) = version.Path is null
                ? await SampleAsync(siteId, target, cancellationToken).ConfigureAwait(false)
                : (null, null, null, version.Path);
            return new Resolved(
                target.DocumentId, new LivePageVersion(version.SchemaVersion, version.Document, version.Title), path, item,
                version.PublishedAtUtc, version.PublishedBy, number, null, false, itemName, null, fromDraft,
                $"{AdminRoot}/sites/{siteId}/pages/{target.DocumentId}/edit");
        }

        var draft = await pages.GetPageDraftAsync(siteId, target.DocumentId, cancellationToken).ConfigureAwait(false);
        if (draft is null)
        {
            return new PreviewOutcome.NotFound();
        }

        // PV-15: a draft that breaks the page schema cannot be rendered faithfully.
        var validation = validator.Validate(Encoding.UTF8.GetBytes(draft.Document.GetRawText()), DocumentKind.Page);
        if (!validation.IsValid)
        {
            return new PreviewOutcome.InvalidDraft(
                validation.Code ?? "page.invalid", validation.Rule ?? string.Empty, validation.Location ?? string.Empty, validation.Message ?? string.Empty);
        }

        var sample = draft.IsTemplate
            ? await SampleAsync(siteId, target, cancellationToken).ConfigureAwait(false)
            : (null, null, null, draft.Path ?? "/");
        return new Resolved(
            target.DocumentId, new LivePageVersion(draft.SchemaVersion, draft.Document, draft.Title), sample.Path, sample.Item,
            draft.SavedAtUtc, draft.SavedBy, null, draft.PendingScheduleAtUtc, false, sample.Name, null, sample.FromDraft,
            $"{AdminRoot}/sites/{siteId}/pages/{target.DocumentId}/edit");
    }

    /// <summary>A template page's sample item (PV-03): published values, or the draft's when it has none.</summary>
    private async Task<(PublishedItem? Item, string? Name, PreviewDraftPart? FromDraft, string Path)> SampleAsync(
        Guid siteId, PreviewTarget target, CancellationToken cancellationToken)
    {
        if (target.SampleItemId is not { } itemId)
        {
            return (null, null, null, "/");
        }

        var published = (await publishedItems.GetAsync(siteId, [itemId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(itemId);
        var draft = published is null ? await items.GetDraftAsync(siteId, itemId, cancellationToken).ConfigureAwait(false) : null;
        if (PreviewComposition.SampleItem(published, draft) is not { } sample)
        {
            return (null, null, null, "/");
        }

        var template = await templates.GetTemplateAsync(siteId, sample.Item.CollectionId, cancellationToken).ConfigureAwait(false);
        var path = template is null ? "/" : template.AddressOf(sample.Item.Slug);
        return (sample.Item, sample.Item.Name ?? sample.Item.Slug, sample.FromDraft, path);
    }

    private async Task<object> ResolveItemAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        var siteId = request.Site.Id;
        var target = request.Target;

        PublishedItem item;
        DateTimeOffset at;
        Guid? by;
        DateTimeOffset? scheduled = null;
        var needsAttention = false;
        if (target.VersionNumber is { } number)
        {
            var version = await items.GetVersionAsync(siteId, target.DocumentId, number, cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                return new PreviewOutcome.NotFound();
            }

            (item, at, by) = (version.Item, version.PublishedAtUtc, version.PublishedBy);
        }
        else
        {
            var draft = await items.GetDraftAsync(siteId, target.DocumentId, cancellationToken).ConfigureAwait(false);
            if (draft is null)
            {
                return new PreviewOutcome.NotFound();
            }

            (item, at, by, scheduled, needsAttention) = (draft.Item, draft.SavedAtUtc, draft.SavedBy, draft.PendingScheduleAtUtc, draft.NeedsAttention);
        }

        // PV-03: the template's live version, or its draft when it was never published.
        var info = await templates.GetTemplateAsync(siteId, item.CollectionId, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return new PreviewOutcome.NoTemplate();
        }

        var liveTemplate = info.IsLive
            ? await pages.GetLiveTemplateVersionAsync(siteId, info.PageId, cancellationToken).ConfigureAwait(false)
            : null;
        var draftTemplate = liveTemplate is null
            ? await pages.GetTemplateDraftAsync(siteId, item.CollectionId, cancellationToken).ConfigureAwait(false)
            : null;
        if (PreviewComposition.Template(liveTemplate, draftTemplate) is not { } template)
        {
            return new PreviewOutcome.NoTemplate();
        }

        return new Resolved(
            template.PageId, template.Version, info.AddressOf(item.Slug), item, at, by, target.VersionNumber, scheduled, needsAttention,
            null, template.FromDraft, null,
            $"{AdminRoot}/sites/{siteId}/collections/{item.CollectionId}/items/{item.Id}");
    }

    private static string ShareAddress(Resolved target) =>
        target.EditAddress + "?share=1" + (target.VersionNumber is { } number ? $"&version={number}" : string.Empty);

    /// <summary>A target resolved to what the composer renders, and what the bar says about it.</summary>
    private sealed record Resolved(
        Guid PageId,
        LivePageVersion Version,
        string Path,
        PublishedItem? Item,
        DateTimeOffset At,
        Guid? By,
        int? VersionNumber,
        DateTimeOffset? ScheduledAt,
        bool NeedsAttention,
        string? SampleItemName,
        PreviewDraftPart? TemplateFromDraft,
        PreviewDraftPart? SampleFromDraft,
        string EditAddress);
}
