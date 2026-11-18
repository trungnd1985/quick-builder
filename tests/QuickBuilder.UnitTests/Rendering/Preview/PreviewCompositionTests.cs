using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.Preview;

namespace QuickBuilder.UnitTests.Rendering.Preview;

/// <summary>
/// What a preview is composed of, one test per row of 022 research PV-03 (spec clarification Q2):
/// live versions where they exist, drafts only for parts never published, and each such part named.
/// </summary>
public sealed class PreviewCompositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 42, 0, TimeSpan.Zero);
    private static readonly Guid Header = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Footer = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid MainMenu = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    [Fact]
    public void A_live_component_wins_over_its_draft_and_a_never_published_one_comes_from_its_draft()
    {
        var live = new Dictionary<Guid, LiveDocument> { [Header] = new(1, Json("""{"live":true}""")) };
        var drafts = new Dictionary<Guid, PreviewDraftDocument>
        {
            [Header] = new(1, Json("""{"draft":true}"""), "Header"),
            [Footer] = new(1, Json("""{"draft":true}"""), "Footer"),
        };

        var composed = PreviewComposition.Components(live, drafts);

        Assert.Contains("live", composed.Documents[Header].Document.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("draft", composed.Documents[Footer].Document.GetRawText(), StringComparison.Ordinal);
        Assert.Equal([new PreviewDraftPart(PreviewPartKind.Component, "Footer")], composed.FromDrafts);
    }

    [Fact]
    public void A_never_published_menu_comes_from_its_draft_with_its_name()
    {
        var composed = PreviewComposition.Menus(
            new Dictionary<Guid, LiveMenu>(),
            new Dictionary<Guid, PreviewDraftDocument> { [MainMenu] = new(1, Json("""{"entries":[]}"""), "Main") });

        Assert.Equal("Main", composed.Documents[MainMenu].Name);
        Assert.Equal([new PreviewDraftPart(PreviewPartKind.Menu, "Main")], composed.FromDrafts);
    }

    [Fact]
    public void The_saved_design_is_shown_only_while_no_design_was_ever_published()
    {
        var defaults = new PublishedDesign(0, 1, Json("""{"defaults":true}"""));
        var published = new PublishedDesign(3, 1, Json("""{"published":true}"""));
        var saved = new DesignTokenSetDto(Guid.NewGuid(), Json("""{"saved":true}"""), 4, IsDefault: false, Now, Guid.NewGuid());
        var never = saved with { IsDefault = true, Document = Json("""{"defaults":true}""") };

        var (fromSaved, savedPart) = PreviewComposition.Design(defaults, saved);
        var (fromPublished, publishedPart) = PreviewComposition.Design(published, saved);
        var (fromDefaults, defaultsPart) = PreviewComposition.Design(defaults, never);

        Assert.Contains("saved", fromSaved.Document.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(new PreviewDraftPart(PreviewPartKind.Design, string.Empty), savedPart);
        Assert.Same(published, fromPublished);
        Assert.Null(publishedPart);
        Assert.Same(defaults, fromDefaults);
        Assert.Null(defaultsPart);
    }

    [Fact]
    public void An_item_preview_uses_the_live_template_or_its_draft_when_it_was_never_published()
    {
        var page = Guid.NewGuid();
        var draft = new PreviewPage(page, "Post", null, true, Guid.NewGuid(), 1, Json("""{"draft":true}"""), Now, Guid.NewGuid(), null, IsLive: false);
        var live = new PreviewPageVersion(page, 2, "Post", null, 1, Json("""{"live":true}"""), Now, Guid.NewGuid());

        var fromLive = PreviewComposition.Template(live, draft with { IsLive = true });
        var fromDraft = PreviewComposition.Template(null, draft);

        Assert.Contains("live", fromLive!.Value.Version.Document.GetRawText(), StringComparison.Ordinal);
        Assert.Null(fromLive.Value.FromDraft);
        Assert.Contains("draft", fromDraft!.Value.Version.Document.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(new PreviewDraftPart(PreviewPartKind.Template, "Post"), fromDraft.Value.FromDraft);
        Assert.Null(PreviewComposition.Template(null, null));
    }

    [Fact]
    public void A_template_page_shows_the_sample_items_published_values_or_its_draft_when_it_has_none()
    {
        var id = Guid.NewGuid();
        var published = new PublishedItem(id, Guid.NewGuid(), "hello", "Published name", Now, Now, Json("""{"published":true}"""));
        var draft = new PreviewItem(published with { Name = "Draft name", Values = Json("""{"draft":true}""") }, Now, null, false, null, IsPublished: false);

        var fromPublished = PreviewComposition.SampleItem(published, draft with { IsPublished = true });
        var fromDraft = PreviewComposition.SampleItem(null, draft);

        Assert.Equal("Published name", fromPublished!.Value.Item.Name);
        Assert.Null(fromPublished.Value.FromDraft);
        Assert.Equal("Draft name", fromDraft!.Value.Item.Name);
        Assert.Equal(new PreviewDraftPart(PreviewPartKind.SampleItem, "Draft name"), fromDraft.Value.FromDraft);
        Assert.Null(PreviewComposition.SampleItem(null, null));
    }
}
