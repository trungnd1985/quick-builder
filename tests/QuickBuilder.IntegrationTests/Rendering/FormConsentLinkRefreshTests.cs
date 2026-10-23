using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Infrastructure;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// A consent's link follows its page (018 analysis H1; constitution Principle VIII): the page it
/// links to is a dependency of the page that shows the form, so moving or unpublishing it
/// refreshes exactly that page, and visitors never follow the form into a not-found page.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FormConsentLinkRefreshTests(DatabaseFixture database)
{
    [Fact]
    public async Task Moving_or_unpublishing_the_linked_page_refreshes_the_page_with_the_form_and_no_other()
    {
        using var world = await Forms.FormsWorld.CreateAsync(database, "form-consent-refresh").ConfigureAwait(true);
        var privacy = await world.PageAsync("privacy").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        var about = await world.PageAsync("about").ConfigureAwait(true);
        var consent = $$"""{"id":"f","type":"form","props":{"name":"F","fields":[{"id":"a00000000001","type":"consent","label":"Consent","statement":"I agree.","linkPageId":"{{privacy}}"}]},"children":[]}""";

        await world.PublishPageAsync(privacy, Document()).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(consent)).ConfigureAwait(true);
        await world.PublishPageAsync(about, Document()).ConfigureAwait(true);
        Assert.Contains("href=\"/privacy\"", await VisitAsync(world, "/contact").ConfigureAwait(true), StringComparison.Ordinal);
        var aboutGeneration = await GenerationAsync(world, about).ConfigureAwait(true);

        var node = await PagesApi.NodeAsync(world.Admin, world.Site, "/privacy").ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(
            world.Admin, world.Site, privacy, node.GetProperty("title").GetString()!, "privacy-policy", node.GetProperty("version").GetInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("href=\"/privacy-policy\"", await VisitAsync(world, "/contact").ConfigureAwait(true), StringComparison.Ordinal);

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, privacy)).ConfigureAwait(true)).ConfigureAwait(true);
        var html = await VisitAsync(world, "/contact").ConfigureAwait(true);
        Assert.DoesNotContain("qb-form__link", html, StringComparison.Ordinal);
        Assert.Contains("I agree.", html, StringComparison.Ordinal);

        Assert.Equal(aboutGeneration, await GenerationAsync(world, about).ConfigureAwait(true));
    }

    private static Task<string> VisitAsync(Forms.FormsWorld world, string path) =>
        world.App.CreateClientForHost(CmsScenario.HostA).GetStringAsync(new Uri(path, UriKind.Relative));

    private static async Task<long> GenerationAsync(Forms.FormsWorld world, Guid pageId)
    {
        await using var scope = world.App.Services.CreateAsyncScope();
        var pages = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        return await PagesScenario.Everything<QuickBuilder.Pages.Domain.Publication>(pages)
            .Where(p => p.SubjectKind == "page" && p.SubjectId == pageId)
            .Select(p => p.Generation)
            .SingleAsync()
            .ConfigureAwait(true);
    }
}
