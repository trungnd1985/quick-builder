using System.Net;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Counting the blocks that use a design token before it is deleted (011 clarification Q4,
/// FR-031, research ED-16).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TokenUsageApiTests(DatabaseFixture database)
{
    /// <summary>
    /// A page with: a section using the token for its background, a heading using it for its
    /// colour, and a text whose content is the token id as words — which must not count.
    /// </summary>
    private const string PageDocument = """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"s","type":"section","props":{"background":{"token":"color-accent"},"paddingY":{"token":"space-xl"}},"children":[
            {"id":"h","type":"heading","props":{"text":"Hi","color":{"token":"color-accent"}},"children":[]},
            {"id":"t","type":"text","props":{"text":"color-accent"},"children":[]},
            {"id":"c","type":"container","props":{"background":{"token":"color-accent"},"gap":{"token":"color-accent"}},"children":[]}]}]}}
        """;

    private const string ComponentDocument = """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"s","type":"section","props":{"background":{"token":"color-accent"}},"children":[]}]}}
        """;

    [Fact]
    public async Task Blocks_are_counted_once_each_over_page_and_component_drafts()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-tokens-count").ConfigureAwait(true);
        var page = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true)).GetProperty("id").GetGuid();
        var component = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteA, page), PageDocument).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, PagesApi.ComponentPath(scenario.SiteA, component), ComponentDocument).ConfigureAwait(true)).ConfigureAwait(true);

        var usage = await EditorApi.TokenUsageOkAsync(scenario.Admin, scenario.SiteA, "color-accent").ConfigureAwait(true);

        // The section, the heading and the container on the page (the container once, though
        // it uses the token twice), and the component's section. Not the text.
        Assert.Equal(("color-accent", 4, 2), (usage.GetProperty("tokenId").GetString(), usage.GetProperty("blockCount").GetInt32(), usage.GetProperty("documentCount").GetInt32()));

        var spacing = await EditorApi.TokenUsageOkAsync(scenario.Admin, scenario.SiteA, "space-xl").ConfigureAwait(true);
        Assert.Equal((1, 1), (spacing.GetProperty("blockCount").GetInt32(), spacing.GetProperty("documentCount").GetInt32()));
    }

    /// <summary>021 T056: what is in the trash is deleted for every count.</summary>
    [Fact]
    public async Task Blocks_in_the_trash_stop_counting()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-tokens-trash").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var page = (await PagesApi.CreateOkAsync(admin, site, "home", isHome: true).ConfigureAwait(true)).GetProperty("id").GetGuid();
        var other = (await PagesApi.CreateOkAsync(admin, site, "other").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var component = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(admin, PagesApi.PagePath(site, other), PageDocument).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(admin, PagesApi.ComponentPath(site, component), ComponentDocument).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.NotEqual(page, other);

        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, other, 1).ConfigureAwait(true)).ConfigureAwait(true);
        var usage = await EditorApi.TokenUsageOkAsync(admin, site, "color-accent").ConfigureAwait(true);
        Assert.Equal((1, 1), (usage.GetProperty("blockCount").GetInt32(), usage.GetProperty("documentCount").GetInt32()));

        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, component).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, component, version).ConfigureAwait(true)).ConfigureAwait(true);
        usage = await EditorApi.TokenUsageOkAsync(admin, site, "color-accent").ConfigureAwait(true);
        Assert.Equal((0, 0), (usage.GetProperty("blockCount").GetInt32(), usage.GetProperty("documentCount").GetInt32()));
    }

    [Fact]
    public async Task A_token_nothing_uses_counts_zero()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-tokens-zero").ConfigureAwait(true);
        await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true);

        var usage = await EditorApi.TokenUsageOkAsync(scenario.Admin, scenario.SiteA, "color-nothing").ConfigureAwait(true);

        Assert.Equal((0, 0), (usage.GetProperty("blockCount").GetInt32(), usage.GetProperty("documentCount").GetInt32()));
    }

    [Theory]
    [InlineData("Color-Brand")]
    [InlineData("color_brand")]
    [InlineData("color'brand")]
    public async Task An_id_outside_the_grammar_is_refused(string tokenId)
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-tokens-badid").ConfigureAwait(true);

        var problem = await PagesApi.ProblemAsync(await EditorApi.TokenUsageAsync(scenario.Admin, scenario.SiteA, Uri.EscapeDataString(tokenId)).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode.BadRequest, "pages.invalid_token_id"), (problem.Status, problem.Code));
    }

    [Fact]
    public async Task Another_sites_drafts_are_never_counted()
    {
        // The query is raw SQL, which the module's query filters do not reach, so the site is
        // filtered by hand; this proves the filter is there (Technology Constraints).
        using var scenario = await PagesScenario.CreateAsync(database, "pg-tokens-sites").ConfigureAwait(true);
        var pageOfB = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteB, "home", isHome: true).ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(scenario.Admin, PagesApi.PagePath(scenario.SiteB, pageOfB), PageDocument).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "home", isHome: true).ConfigureAwait(true);

        var onA = await EditorApi.TokenUsageOkAsync(scenario.Admin, scenario.SiteA, "color-accent").ConfigureAwait(true);
        var onB = await EditorApi.TokenUsageOkAsync(scenario.Admin, scenario.SiteB, "color-accent").ConfigureAwait(true);

        Assert.Equal(0, onA.GetProperty("blockCount").GetInt32());
        Assert.Equal(3, onB.GetProperty("blockCount").GetInt32());
    }

    [Fact]
    public async Task An_unknown_site_is_not_found()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "pg-tokens-nosite").ConfigureAwait(true);

        var problem = await PagesApi.ProblemAsync(await EditorApi.TokenUsageAsync(scenario.Admin, Guid.CreateVersion7(), "color-brand").ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode.NotFound, "sites.not_found"), (problem.Status, problem.Code));
    }
}
