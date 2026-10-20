using System.Net.Http.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// The site-wide menu style reaches visitors with the design, and block overrides survive it
/// (024 FR-017a, research NV-25 to NV-28; tasks T099).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MenuStyleRenderTests(DatabaseFixture database)
{
    [Fact]
    public async Task Publishing_the_design_restyles_menus_that_do_not_override_it()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-style").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("a", UrlTarget("https://a.example/"), label: "A"))).ConfigureAwait(true);
        await NavigationRenderTests.PublishMenuAsync(world, menu).ConfigureAwait(true);

        // A header without overrides, and a footer that sets its own look and bar background.
        var header = await NavigationRenderTests.HeaderShowingAsync(world, menu).ConfigureAwait(true);
        var home = await world.PageAsync("home").ConfigureAwait(true);
        await world.PublishDocumentAsync(home, PublishingApi.HeadingDocument("Home", header)).ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishDocumentAsync(contact, PageShowing(menu, """ "look": "underline", "background": { "token": "color-brand-light" } """)).ConfigureAwait(true);

        var homeBefore = await NavigationRenderTests.GetAsync(world, "/home").ConfigureAwait(true);
        var contactBefore = await NavigationRenderTests.GetAsync(world, "/contact").ConfigureAwait(true);
        Assert.Contains("qb-nav--look-link\"", homeBefore.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("--qb-menu-", homeBefore.Body, StringComparison.Ordinal);

        // Saved but not published: visitors see nothing new (Principle VII).
        var tokens = JsonNode.Parse(EditorApi.DefaultDocument)!.AsObject();
        tokens["menuStyle"] = JsonNode.Parse("""{ "look": "pill", "colors": { "background": "color-text", "text": "color-background" } }""");
        var saved = await PagesApi.OkAsync(await EditorApi.SaveCurrentAsync(world.Admin, world.Site, tokens.ToJsonString()).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(homeBefore.ETag, (await NavigationRenderTests.GetAsync(world, "/home").ConfigureAwait(true)).ETag);

        // Published: both pages render anew.
        using (var published = await world.Admin.PostAsJsonAsync(
            new Uri($"{EditorApi.TokensPath(world.Site)}/publish", UriKind.Relative),
            new { version = saved.GetProperty("version").GetUInt32() }).ConfigureAwait(true))
        {
            Assert.True(published.IsSuccessStatusCode, $"Publishing the design gave {(int)published.StatusCode}");
        }

        var homeAfter = await NavigationRenderTests.GetAsync(world, "/home").ConfigureAwait(true);
        var contactAfter = await NavigationRenderTests.GetAsync(world, "/contact").ConfigureAwait(true);
        Assert.NotEqual(homeBefore.ETag, homeAfter.ETag);
        Assert.NotEqual(contactBefore.ETag, contactAfter.ETag);

        // The site layer is in the token CSS; the header follows the site's look.
        Assert.Contains("--qb-menu-background:var(--qb-color-color-text);--qb-menu-text:var(--qb-color-color-background);", homeAfter.Body, StringComparison.Ordinal);
        Assert.Contains("qb-nav--look-pill\"", homeAfter.Body, StringComparison.Ordinal);

        // The footer keeps its own look and background.
        Assert.Contains("qb-nav--look-underline\"", contactAfter.Body, StringComparison.Ordinal);
        Assert.Contains("--qb-nav-background:var(--qb-color-color-brand-light);", contactAfter.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("qb-nav--look-pill", contactAfter.Body, StringComparison.Ordinal);
    }
}
