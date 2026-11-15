using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 5: where a file is used, by place, with counts that always agree with the
/// usage reader (013 FR-014, SC-008, contracts/media-api.yaml <c>getMediaFileUsage</c>).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FileUsageApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "file-usage").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Places_are_listed_with_draft_live_history_and_component_flags()
    {
        var admin = _scenario.Admin;
        var site = _scenario.SiteA;
        var file = (await MediaApi.UploadOkAsync(admin, site)).Id;

        var draft = await UsageScenario.PageShowingAsync(admin, site, "draft-only", file, publish: false);
        var live = await UsageScenario.PageShowingAsync(admin, site, "live", file, publish: true);

        var banner = (await PagesApi.CreateComponentOkAsync(admin, site, "Banner")).GetProperty("id").GetGuid();
        var bannerPath = PagesApi.ComponentPath(site, banner);
        await PublishingApi.SaveAsync(admin, bannerPath, MediaUsageReaderTests.Image(file));
        await PublishingApi.PublishCurrentAsync(admin, bannerPath);
        var shows = (await PagesApi.SeedTreeAsync(admin, site, "shows-banner"))["/shows-banner"];
        await PublishingApi.SaveAsync(admin, PagesApi.PagePath(site, shows), PublishingApi.HeadingDocument("Banner page", banner));
        await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(site, shows));

        var usage = await UsageScenario.UsageAsync(admin, site, file);

        var summary = usage.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("places").GetInt32());
        Assert.Equal(2, summary.GetProperty("livePlaces").GetInt32());
        Assert.True(summary.GetProperty("inLiveUse").GetBoolean());

        var places = usage.GetProperty("places").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());
        Assert.Equal(3, places.Count);
        Assert.Equal(("page", true, false, false), Flags(places[draft]));
        Assert.Equal(("page", true, true, false), Flags(places[live]));
        Assert.Equal(("component", true, true, false), Flags(places[banner]));
        Assert.Equal("Banner", places[banner].GetProperty("title").GetString());
        Assert.Equal(1, places[banner].GetProperty("livePagesThroughComponent").GetInt32());
        Assert.Equal(JsonValueKind.Null, places[live].GetProperty("livePagesThroughComponent").ValueKind);
    }

    [Fact]
    public async Task Counts_follow_draft_edits_publishing_rollback_and_deletion()
    {
        var admin = _scenario.Admin;
        var site = _scenario.SiteA;
        var file = (await MediaApi.UploadOkAsync(admin, site)).Id;
        var other = (await MediaApi.UploadOkAsync(admin, site)).Id;

        var page = await UsageScenario.PageShowingAsync(admin, site, "history", file, publish: false);
        var path = PagesApi.PagePath(site, page);
        await AssertCountsAsync(file, places: 1, draft: 1, live: 0, historyOnly: 0);

        await PublishingApi.PublishCurrentAsync(admin, path);
        await AssertCountsAsync(file, places: 1, draft: 1, live: 1, historyOnly: 0);

        await PublishingApi.SaveAsync(admin, path, MediaUsageReaderTests.Image(other));
        await PublishingApi.PublishCurrentAsync(admin, path);
        await AssertCountsAsync(file, places: 1, draft: 0, live: 0, historyOnly: 1);

        var rolledBack = await PublishingApi.RollbackAsync(admin, path, 1);
        Assert.True(rolledBack.IsSuccessStatusCode, await rolledBack.Content.ReadAsStringAsync());
        await AssertCountsAsync(file, places: 1, draft: 0, live: 1, historyOnly: 0);

        var draftOnly = await UsageScenario.PageShowingAsync(admin, site, "draft-only", other, publish: false);
        // The rollback made version 1 live again, so the other file is in drafts only.
        await AssertCountsAsync(other, places: 2, draft: 2, live: 0, historyOnly: 0);
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.DeleteAsync(admin, site, draftOnly, 1)).StatusCode);
        await AssertCountsAsync(other, places: 1, draft: 1, live: 0, historyOnly: 0, trashed: 1);
    }

    [Fact]
    public async Task Without_page_content_access_only_the_counts_are_given()
    {
        var file = (await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA)).Id;
        await UsageScenario.PageShowingAsync(_scenario.Admin, _scenario.SiteA, "secret-launch", file, publish: false);

        using var restricted = new QuickBuilderApp(
            Connection(),
            configureTestServices: services => UsageScenario.Withhold(services, PageContentCapabilities.View),
            storagePath: _scenario.App.StoragePath);
        var editor = await IdentitySeed.SeedUserAsync(restricted.Services, siteRoles: [(_scenario.SiteA, QuickBuilder.Shared.Kernel.Authorization.SystemRole.Editor)]);
        using var client = await IdentitySeed.CreateSignedInClientAsync(restricted, editor.Id);

        var usage = await UsageScenario.UsageAsync(client, _scenario.SiteA, file);

        Assert.Equal(1, usage.GetProperty("summary").GetProperty("places").GetInt32());
        Assert.Empty(usage.GetProperty("places").EnumerateArray());
        Assert.DoesNotContain("secret-launch", usage.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_sites_file_is_not_found()
    {
        var file = (await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB)).Id;

        var response = await _scenario.Admin.GetAsync(new Uri(UsageScenario.UsagePath(_scenario.SiteA, file), UriKind.Relative));

        await LibraryApi.RefusedAsync(response, HttpStatusCode.NotFound, "media.not_found");
    }

    private async Task AssertCountsAsync(Guid file, int places, int draft, int live, int historyOnly, int trashed = 0)
    {
        var summary = (await UsageScenario.UsageAsync(_scenario.Admin, _scenario.SiteA, file)).GetProperty("summary");
        var shown = new MediaUsageSummary(
            summary.GetProperty("places").GetInt32(),
            summary.GetProperty("draftPlaces").GetInt32(),
            summary.GetProperty("livePlaces").GetInt32(),
            summary.GetProperty("historyOnlyPlaces").GetInt32(),
            summary.GetProperty("trashedPlaces").GetInt32());

        Assert.Equal(new MediaUsageSummary(places, draft, live, historyOnly, trashed), shown);

        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var read = await scope.ServiceProvider.GetRequiredService<IMediaUsageReader>().SummariseAsync(_scenario.SiteA, [file]);
        Assert.Equal(read[file], shown);
    }

    private string Connection() =>
        _scenario.App.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["ConnectionStrings:Default"]!;

    private static (string Kind, bool InDraft, bool Live, bool InHistory) Flags(JsonElement place) => (
        place.GetProperty("kind").GetString()!,
        place.GetProperty("inDraft").GetBoolean(),
        place.GetProperty("live").GetBoolean(),
        place.GetProperty("inHistory").GetBoolean());
}
