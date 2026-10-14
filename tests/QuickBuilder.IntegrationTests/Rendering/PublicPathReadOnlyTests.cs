using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// The public path cannot write content: PostgreSQL refuses it (constitution Principle VII,
/// 012 research PR-06).
/// </summary>
/// <remarks>
/// Every reader the renderer uses runs on its own context over a read-only connection. These
/// tests attempt a write through each and expect the database, not the code, to refuse it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PublicPathReadOnlyTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "readonlypath").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("pages", "UPDATE pages.publications SET generation = generation + 1")]
    [InlineData("editor", "UPDATE editor.design_publications SET live_number = live_number")]
    [InlineData("media", "UPDATE media.media_files SET file_name = file_name")]
    public async Task The_database_refuses_a_write_through_a_reader_context(string module, string sql)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(_scenario.SiteA);

        DbContext context = module switch
        {
            "pages" => scope.ServiceProvider.GetRequiredService<PublishedReadDbContext>(),
            "editor" => scope.ServiceProvider.GetRequiredService<PublishedDesignReadDbContext>(),
            _ => scope.ServiceProvider.GetRequiredService<PublicMediaReadDbContext>(),
        };

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql)).ConfigureAwait(true);

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
    }

    [Fact]
    public async Task Saving_through_a_reader_context_is_refused_before_the_database()
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();

        foreach (var context in new DbContext[]
                 {
                     scope.ServiceProvider.GetRequiredService<PublishedReadDbContext>(),
                     scope.ServiceProvider.GetRequiredService<PublishedDesignReadDbContext>(),
                     scope.ServiceProvider.GetRequiredService<PublicMediaReadDbContext>(),
                 })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync()).ConfigureAwait(true);
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    [Fact]
    public async Task A_reader_never_returns_another_sites_pages()
    {
        var page = await PagesApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(true);
        var pageId = page.GetProperty("id").GetGuid();

        // Resolved to site A, asking for site A's page: it is there (not live, but it exists).
        Assert.True((await LinkTargetsAsync(resolvedSite: _scenario.SiteA, askedSite: _scenario.SiteA, pageId).ConfigureAwait(true))
            .ContainsKey(pageId));

        // Resolved to site B: site A's page is invisible, whichever site the caller names.
        Assert.Empty(await LinkTargetsAsync(resolvedSite: _scenario.SiteB, askedSite: _scenario.SiteA, pageId).ConfigureAwait(true));
        Assert.Empty(await LinkTargetsAsync(resolvedSite: _scenario.SiteB, askedSite: _scenario.SiteB, pageId).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_link_target_carries_its_live_title_only_while_live()
    {
        // 024 NV-09: menu entries without their own label show the title visitors see.
        var page = await PagesApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "services").ConfigureAwait(true);
        var pageId = page.GetProperty("id").GetGuid();

        var draftOnly = (await LinkTargetsAsync(_scenario.SiteA, _scenario.SiteA, pageId).ConfigureAwait(true))[pageId];
        Assert.False(draftOnly.IsLive);
        Assert.Null(draftOnly.LiveTitle);

        await Publishing.PublishingApi.PublishCurrentAsync(_scenario.Admin, Publishing.PublishingApi.Subject(_scenario.SiteA, "page", pageId)).ConfigureAwait(true);

        var live = (await LinkTargetsAsync(_scenario.SiteA, _scenario.SiteA, pageId).ConfigureAwait(true))[pageId];
        Assert.True(live.IsLive);
        Assert.Equal(PagesApi.Title("services"), live.LiveTitle);
    }

    [Fact]
    public async Task Menus_without_a_live_version_and_other_sites_menus_are_absent()
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(_scenario.SiteA);
        var reader = scope.ServiceProvider.GetRequiredService<IPublishedContentReader>();

        Assert.Empty(await reader.GetLiveMenusAsync(_scenario.SiteA, [Guid.NewGuid()]).ConfigureAwait(true));
        Assert.Empty(await reader.GetLiveMenusAsync(_scenario.SiteA, []).ConfigureAwait(true));
    }

    private async Task<IReadOnlyDictionary<Guid, PageLinkTarget>> LinkTargetsAsync(Guid resolvedSite, Guid askedSite, Guid pageId)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(resolvedSite);

        var reader = scope.ServiceProvider.GetRequiredService<IPublishedContentReader>();
        return await reader.GetLinkTargetsAsync(askedSite, [pageId]).ConfigureAwait(true);
    }
}
