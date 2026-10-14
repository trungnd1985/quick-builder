using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// Warnings, never refusals: a query redirect over live content or a list's page parameter
/// (017 FR-017a), and a page that will answer a redirect's source first (spec edge case;
/// data-model §4).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectWarningTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _blog;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-redirect-warnings");
        await _world.PublishedPostAsync("hello");
        _blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(_blog, TemplatesWorld.Document(
            TemplatesWorld.ListOf("posts", _world.Posts, """ "paging":{"pageSize":10} """, TemplatesWorld.BoundHeading("t", """{"field":"f1"}"""))));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private static string[] Warnings(JsonElement redirect) =>
        [.. redirect.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!)];

    [Fact]
    public async Task A_query_redirect_over_a_live_page_using_its_page_parameter_is_warned_about()
    {
        var created = await RedirectsApi.CreateOkAsync(_world.Admin, _world.Site, "/blog?page=2", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        Assert.Equal(["source_holds_content", "source_uses_page_param"], Warnings(created));
    }

    [Fact]
    public async Task A_query_redirect_elsewhere_has_no_warnings()
    {
        var created = await RedirectsApi.CreateOkAsync(_world.Admin, _world.Site, "/old.php?id=1", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        Assert.Empty(Warnings(created));
    }

    [Fact]
    public async Task A_page_created_at_a_redirect_s_source_says_it_will_hide_it()
    {
        var redirect = await RedirectsApi.CreateOkAsync(_world.Admin, _world.Site, "/landing", RedirectsApi.ToPath("/x")).ConfigureAwait(true);

        var response = await PagesApi.CreateAsync(_world.Admin, _world.Site, new { title = "Landing", slug = "landing" }).ConfigureAwait(true);
        var page = await PagesApi.OkAsync(response, HttpStatusCode.Created).ConfigureAwait(true);

        var warning = Assert.Single(page.GetProperty("warnings").EnumerateArray());
        Assert.Equal("redirect_hidden", warning.GetProperty("code").GetString());
        Assert.Equal(redirect.GetProperty("id").GetGuid(), warning.GetProperty("redirectId").GetGuid());
    }

    [Fact]
    public async Task Publishing_a_page_at_a_redirect_s_source_says_it_hides_it()
    {
        var pageId = await _world.PageAsync("campaign");
        await RedirectsApi.CreateOkAsync(_world.Admin, _world.Site, "/campaign", RedirectsApi.ToPath("/x")).ConfigureAwait(true);
        var path = PagesApi.PagePath(_world.Site, pageId);
        await PublishingApi.SaveAsync(_world.Admin, path, PublishingApi.HeadingDocument("Campaign")).ConfigureAwait(true);

        var publication = await PublishingApi.PublishCurrentAsync(_world.Admin, path).ConfigureAwait(true);

        Assert.Equal("redirect_hidden", publication.GetProperty("warnings")[0].GetProperty("code").GetString());
    }
}
