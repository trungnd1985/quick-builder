using System.Text.Json;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Pages.Contracts.Publishing;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Finds the form a submission is for, as visitors are served it (018 FR-015; research FS-07).
/// </summary>
/// <remarks>
/// The form must be in the live version of the page the submission names, or of a live
/// component that page shows. It is looked up on every submission, through the public path's own
/// reader, rather than in the registry, which follows publishes an outbox tick later (FS-05):
/// a form unpublished a moment ago is refused at once.
/// </remarks>
internal sealed class LiveFormLocator(IPublishedContentReader pages)
{
    /// <summary>The live form, and the address the submission is recorded under; null when the form is not live there.</summary>
    public async Task<LiveForm?> FindAsync(Guid siteId, string formId, string? pageIdText, string? postedPath, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(pageIdText, out var pageId))
        {
            return null;
        }

        var page = await pages.GetLivePageAsync(siteId, pageId, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return null;
        }

        var form = FormDefinition.Find(page.Document, formId);
        if (form is null)
        {
            var components = ComponentIds(page.Document);
            if (components.Count > 0)
            {
                var live = await pages.GetLiveComponentsAsync(siteId, components, cancellationToken).ConfigureAwait(false);
                form = live.Values.Select(c => FormDefinition.Find(c.Document, formId)).FirstOrDefault(f => f is not null);
            }
        }

        return form is null ? null : new LiveForm(form, pageId, await PathAsync(siteId, pageId, postedPath, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The posted address when it is the page's own, or an item's address under the page as a
    /// template; otherwise the page's own address. A visitor cannot record another page.
    /// </summary>
    private async Task<string> PathAsync(Guid siteId, Guid pageId, string? posted, CancellationToken cancellationToken)
    {
        if (posted is { Length: > 0 and <= Submission.MaxPagePathLength } path && path[0] == '/')
        {
            if (await pages.FindLiveByPathAsync(siteId, path, cancellationToken).ConfigureAwait(false) is { } live && live.PageId == pageId)
            {
                return path;
            }

            var slash = path.LastIndexOf('/');
            if (slash > 0 && slash < path.Length - 1
                && await pages.FindLiveTemplateAsync(siteId, path[..slash], cancellationToken).ConfigureAwait(false) is { } template
                && template.PageId == pageId)
            {
                return path;
            }
        }

        var targets = await pages.GetLinkTargetsAsync(siteId, [pageId], cancellationToken).ConfigureAwait(false);
        return targets.TryGetValue(pageId, out var target) ? target.Path : "/";
    }

    private static List<Guid> ComponentIds(JsonElement document)
    {
        var ids = new List<Guid>();
        if (document.TryGetProperty("root", out var root) && root.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                if (child.TryGetProperty("type", out var type) && type.GetString() == "component-ref"
                    && child.GetProperty("props").TryGetProperty("componentId", out var id) && id.TryGetGuid(out var componentId))
                {
                    ids.Add(componentId);
                }
            }
        }

        return ids;
    }
}

/// <summary>A form as it is live, the page that served it, and the address to record.</summary>
internal sealed record LiveForm(FormDefinition Form, Guid PageId, string Path);
