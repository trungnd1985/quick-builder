using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// The band above every preview that says what the viewer sees (022 research PV-13, spec
/// FR-020, FR-021; contracts/admin-preview.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Written as the first element of <c>&lt;body&gt;</c>, outside the page root, in normal flow:
/// it never overlays the page and changes no rule inside it, so the page lays out exactly as
/// published at every preview width. Its rules go into the page's one style element, every
/// class prefixed <c>qb-preview</c>, with fixed colours and font, independent of the design.
/// </para>
/// <para>
/// Hidden without a script: a visually hidden checkbox and its label. Names and labels are
/// content and are encoded.
/// </para>
/// </remarks>
internal static class PreviewBar
{
    /// <summary>The bar's rules, appended to the page's style element. Never holds <c>&lt;</c>.</summary>
    public const string Css =
        ".qb-preview{position:relative;z-index:2147483647;font:14px/1.45 system-ui,-apple-system,\"Segoe UI\",sans-serif;color:#111827;text-align:start}"
        + ".qb-preview-toggle{position:absolute;width:1px;height:1px;margin:-1px;overflow:hidden;clip-path:inset(50%);white-space:nowrap}"
        + ".qb-preview-bar{display:flex;flex-wrap:wrap;gap:.25rem 1rem;align-items:center;padding:.5rem 1rem;background:#fff7d6;border-bottom:2px solid #b45309}"
        + ".qb-preview-bar p,.qb-preview-bar ul{margin:0;padding:0}"
        + ".qb-preview-notes{list-style:none;display:flex;flex-wrap:wrap;gap:.25rem 1rem}"
        + ".qb-preview-actions{display:flex;gap:.75rem;margin-inline-start:auto}"
        + ".qb-preview-bar a,.qb-preview-hide{color:#7c2d12;text-decoration:underline;cursor:pointer}"
        + ".qb-preview-toggle:focus-visible~.qb-preview-bar .qb-preview-hide{outline:2px solid #7c2d12;outline-offset:2px}"
        + "#qb-preview-hide:checked~.qb-preview-bar{display:none}";

    /// <summary>The bar's markup.</summary>
    public static string Html(PreviewBarModel bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        var writer = new HtmlWriter();
        writer.Open("div").Attribute("class", "qb-preview").Attribute("role", "region").Attribute("aria-label", PreviewTexts.BarLabel).EndTag();
        writer.Markup("<input type=\"checkbox\" id=\"qb-preview-hide\" class=\"qb-preview-toggle\">");
        writer.Open("div").Attribute("class", "qb-preview-bar").EndTag();

        writer.Open("p").Attribute("class", "qb-preview-status").EndTag()
            .Markup("<strong>").Text(PreviewTexts.NotLive).Markup("</strong> · ")
            .Text(Shown(bar))
            .Close("p");

        writer.Open("ul").Attribute("class", "qb-preview-notes").EndTag();
        foreach (var note in Notes(bar))
        {
            writer.Markup("<li>").Text(note).Markup("</li>");
        }

        writer.Close("ul");

        writer.Open("p").Attribute("class", "qb-preview-actions").EndTag();
        if (bar.EditAddress is { } edit && HtmlWriter.IsSitePath(edit))
        {
            writer.Open("a");
            writer.SitePath("href", edit);
            writer.EndTag().Text(PreviewTexts.BackToEditor).Close("a");
        }

        if (bar.ShareAddress is { } share && HtmlWriter.IsSitePath(share))
        {
            writer.Open("a");
            writer.SitePath("href", share);
            writer.EndTag().Text(PreviewTexts.SharePreview).Close("a");
        }

        writer.Markup("<label for=\"qb-preview-hide\" class=\"qb-preview-hide\">").Text(PreviewTexts.HideBar).Markup("</label>");
        writer.Close("p");

        writer.Close("div");
        writer.Close("div");
        return writer.ToString();
    }

    /// <summary>What is shown: the draft and its saver, or a version and its publisher.</summary>
    private static string Shown(PreviewBarModel bar)
    {
        var time = PreviewTexts.Time(bar.At);
        return bar.State == PreviewState.Version
            ? bar.By is { Length: > 0 } publisher
                ? Format(PreviewTexts.VersionPublishedBy, bar.VersionNumber ?? 0, time, publisher)
                : Format(PreviewTexts.VersionPublished, bar.VersionNumber ?? 0, time)
            : bar.By is { Length: > 0 } saver
                ? Format(PreviewTexts.DraftSavedBy, time, saver)
                : Format(PreviewTexts.DraftSaved, time);
    }

    private static IEnumerable<string> Notes(PreviewBarModel bar)
    {
        if (bar.SampleItemName is { Length: > 0 } item)
        {
            yield return Format(PreviewTexts.SampleItem, item);
        }

        foreach (var part in bar.FromDrafts ?? [])
        {
            yield return Format(PreviewTexts.FromDraft, PartName(part));
        }

        if (bar.ScheduledAt is { } scheduled)
        {
            yield return Format(PreviewTexts.Scheduled, PreviewTexts.Time(scheduled));
        }

        if (bar.NeedsAttention)
        {
            yield return PreviewTexts.NeedsAttention;
        }

        if (bar.ExpiresAt is { } expires)
        {
            yield return Format(PreviewTexts.Expires, PreviewTexts.Time(expires));
        }

        yield return PreviewTexts.LinksLeave;
    }

    private static string PartName(PreviewDraftPart part) => part.Kind switch
    {
        PreviewPartKind.Component => Format(PreviewTexts.Component, part.Name),
        PreviewPartKind.Menu => Format(PreviewTexts.Menu, part.Name),
        PreviewPartKind.Template => Format(PreviewTexts.Template, part.Name),
        PreviewPartKind.SampleItem => Format(PreviewTexts.SampleItemPart, part.Name),
        _ => PreviewTexts.Design,
    };

    private static string Format(string text, params object[] values) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, text, values);
}
