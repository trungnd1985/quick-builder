using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// Redirect use is counted in memory and saved in the background; serving a redirect never
/// writes (017 FR-021a, research SE-12).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectUsageTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "seo-redirect-usage").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Hits_are_counted_in_memory_and_saved_by_a_flush()
    {
        var id = await CreateAsync("/counted").ConfigureAwait(true);

        for (var i = 0; i < 3; i++)
        {
            await RedirectsApi.VisitAsync(_scenario.App, PagesScenario.HostA, "/counted").ConfigureAwait(true);
        }

        // Nothing was written while answering: the request path is read-only (Principle VII).
        Assert.Null(await UsageAsync(id).ConfigureAwait(true));

        await FlushAsync(_scenario.App).ConfigureAwait(true);

        var usage = await UsageAsync(id).ConfigureAwait(true);
        Assert.Equal(3, usage!.Value.Hits);

        var read = await PagesApi.OkAsync(await RedirectsApi.GetAsync(_scenario.Admin, _scenario.SiteA, id).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(3, read.GetProperty("hits").GetInt64());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, read.GetProperty("lastUsedAt").ValueKind);
    }

    [Fact]
    public async Task Counts_from_two_instances_add_up()
    {
        var id = await CreateAsync("/shared").ConfigureAwait(true);
        using var second = new QuickBuilderApp(_scenario.App.ConnectionString);

        await RedirectsApi.VisitAsync(_scenario.App, PagesScenario.HostA, "/shared").ConfigureAwait(true);
        await RedirectsApi.VisitAsync(second, PagesScenario.HostA, "/shared").ConfigureAwait(true);
        await RedirectsApi.VisitAsync(second, PagesScenario.HostA, "/shared").ConfigureAwait(true);

        await FlushAsync(_scenario.App).ConfigureAwait(true);
        await FlushAsync(second).ConfigureAwait(true);

        Assert.Equal(3, (await UsageAsync(id).ConfigureAwait(true))!.Value.Hits);
    }

    [Fact]
    public async Task A_second_flush_adds_to_the_first()
    {
        var id = await CreateAsync("/twice").ConfigureAwait(true);

        await RedirectsApi.VisitAsync(_scenario.App, PagesScenario.HostA, "/twice").ConfigureAwait(true);
        await FlushAsync(_scenario.App).ConfigureAwait(true);
        await RedirectsApi.VisitAsync(_scenario.App, PagesScenario.HostA, "/twice").ConfigureAwait(true);
        await FlushAsync(_scenario.App).ConfigureAwait(true);

        Assert.Equal(2, (await UsageAsync(id).ConfigureAwait(true))!.Value.Hits);
    }

    [Fact]
    public async Task A_redirect_deleted_before_the_flush_is_skipped()
    {
        var created = await RedirectsApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "/deleted", RedirectsApi.ToPath("/x")).ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();
        await RedirectsApi.VisitAsync(_scenario.App, PagesScenario.HostA, "/deleted").ConfigureAwait(true);
        await RedirectsApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, id, created.GetProperty("version").GetUInt32()).ConfigureAwait(true);

        await FlushAsync(_scenario.App).ConfigureAwait(true);

        Assert.Null(await UsageAsync(id).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_redirect_whose_target_is_gone_is_not_counted()
    {
        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "never-live").ConfigureAwait(true);
        var created = await RedirectsApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "/to-nothing", RedirectsApi.ToPage(pages["/never-live"])).ConfigureAwait(true);

        await RedirectsApi.VisitAsync(_scenario.App, PagesScenario.HostA, "/to-nothing").ConfigureAwait(true);
        await FlushAsync(_scenario.App).ConfigureAwait(true);

        Assert.Null(await UsageAsync(created.GetProperty("id").GetGuid()).ConfigureAwait(true));
    }

    private async Task<Guid> CreateAsync(string source) =>
        (await RedirectsApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, source, RedirectsApi.ToPath("/x")).ConfigureAwait(false)).GetProperty("id").GetGuid();

    private static Task FlushAsync(QuickBuilderApp app) =>
        app.Services.GetRequiredService<RedirectUsageFlusher>().FlushAsync(CancellationToken.None);

    private Task<(long Hits, DateTimeOffset LastUsed)?> UsageAsync(Guid id) =>
        _scenario.ReadAsync(async db =>
        {
            var row = await PagesScenario.Everything<RedirectUsage>(db).AsNoTracking().FirstOrDefaultAsync(u => u.RedirectId == id).ConfigureAwait(false);
            return row is null ? ((long, DateTimeOffset)?)null : (row.Hits, row.LastUsedAtUtc);
        });
}
