using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Contracts.Media;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Where a file is used, kept current in the same transaction as every draft save, publish
/// and delete (013 research MV-05, data-model.md §2.3, SC-008).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaUsageReaderTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly Guid File = Guid.Parse("0d8e8c0e-7e0b-4a3b-9a55-6a4a2f0b1c01");
    private static readonly Guid Other = Guid.Parse("0d8e8c0e-7e0b-4a3b-9a55-6a4a2f0b1c02");

    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "media-usage").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_file_nothing_uses_has_no_places() =>
        // 021 FR-027: the use is still known, but it never blocks or warns.
        Assert.Equal(MediaUsageSummary.None, (await SummaryAsync(File).ConfigureAwait(true))[File]);

    [Fact]
    public async Task Draft_live_history_and_component_use_are_told_apart()
    {
        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "draft-only\nlive\nhistory\nshows-banner").ConfigureAwait(true);

        // Draft only.
        await SaveAsync(Page(pages["/draft-only"]), Image(File)).ConfigureAwait(true);

        // Live.
        await SaveAsync(Page(pages["/live"]), Image(File)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, Page(pages["/live"])).ConfigureAwait(true);

        // History only: published with the file, then republished and redrafted without it.
        await SaveAsync(Page(pages["/history"]), Image(File)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, Page(pages["/history"])).ConfigureAwait(true);
        await SaveAsync(Page(pages["/history"]), Image(Other)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, Page(pages["/history"])).ConfigureAwait(true);

        // A live component shown by one live page.
        var banner = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Banner").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var bannerPath = PagesApi.ComponentPath(_scenario.SiteA, banner);
        await SaveAsync(bannerPath, Image(File)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, bannerPath).ConfigureAwait(true);
        await SaveAsync(Page(pages["/shows-banner"]), PublishingApi.HeadingDocument("Banner page", banner)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, Page(pages["/shows-banner"])).ConfigureAwait(true);

        var summary = (await SummaryAsync(File).ConfigureAwait(true))[File];
        // draft-only, live, history, Banner.
        Assert.Equal(new MediaUsageSummary(Places: 4, DraftPlaces: 3, LivePlaces: 2, HistoryOnlyPlaces: 1), summary);
        Assert.True(summary.InLiveUse);

        var places = await PlacesAsync(File).ConfigureAwait(true);
        Assert.Equal(["page", "page", "page", "component"], places.Select(p => p.Kind));

        var byId = places.ToDictionary(p => p.Id);
        Assert.Equal((true, false, false), Flags(byId[pages["/draft-only"]]));
        Assert.Equal((true, true, false), Flags(byId[pages["/live"]]));
        Assert.Equal((false, false, true), Flags(byId[pages["/history"]]));
        Assert.Equal((true, true, false), Flags(byId[banner]));
        Assert.Equal("Banner", byId[banner].Title);
        Assert.Equal(1, byId[banner].LivePagesThroughComponent);
        Assert.Null(byId[pages["/live"]].LivePagesThroughComponent);
    }

    [Fact]
    public async Task A_draft_that_stops_using_a_file_updates_usage_in_the_same_request()
    {
        var page = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(true))["/about"];
        await SaveAsync(Page(page), Image(File)).ConfigureAwait(true);
        Assert.Equal(1, (await SummaryAsync(File).ConfigureAwait(true))[File].DraftPlaces);

        await SaveAsync(Page(page), Image(Other)).ConfigureAwait(true);

        var summaries = await SummaryAsync(File, Other).ConfigureAwait(true);
        Assert.Equal(MediaUsageSummary.None, summaries[File]);
        Assert.Equal(1, summaries[Other].DraftPlaces);
    }

    [Fact]
    public async Task A_page_in_the_trash_is_counted_apart_and_never_as_in_use()
    {
        var page = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "gone").ConfigureAwait(true))["/gone"];
        await SaveAsync(Page(page), Image(File)).ConfigureAwait(true);

        var deleted = await PagesApi.DeleteAsync(_scenario.Admin, _scenario.SiteA, page, 1).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        // 021 FR-027: the use is still known, but it never blocks or warns.
        Assert.Equal(MediaUsageSummary.None with { TrashedPlaces = 1 }, (await SummaryAsync(File).ConfigureAwait(true))[File]);
    }

    [Fact]
    public async Task Another_sites_usage_is_never_counted()
    {
        var page = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteB, "elsewhere").ConfigureAwait(true))["/elsewhere"];
        await SaveAsync(PagesApi.PagePath(_scenario.SiteB, page), Image(File)).ConfigureAwait(true);

        // 021 FR-027: the use is still known, but it never blocks or warns.
        Assert.Equal(MediaUsageSummary.None, (await SummaryAsync(File).ConfigureAwait(true))[File]);
        Assert.Empty(await PlacesAsync(File).ConfigureAwait(true));
    }

    private string Page(Guid id) => PagesApi.PagePath(_scenario.SiteA, id);

    private Task SaveAsync(string path, string document) => PublishingApi.SaveAsync(_scenario.Admin, path, document);

    private async Task<IReadOnlyDictionary<Guid, MediaUsageSummary>> SummaryAsync(params Guid[] ids)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaUsageReader>().SummariseAsync(_scenario.SiteA, ids).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MediaUsagePlace>> PlacesAsync(Guid id)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaUsageReader>().ListAsync(_scenario.SiteA, id).ConfigureAwait(false);
    }

    private static (bool InDraft, bool Live, bool InHistory) Flags(MediaUsagePlace place) => (place.InDraft, place.Live, place.InHistory);

    /// <summary>A document with one Section holding an Image of <paramref name="file"/>.</summary>
    internal static string Image(Guid file) =>
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":[{"id":"i1","type":"image","props":{"media":{"mediaId":"""
        + JsonSerializer.Serialize(file.ToString())
        + """},"alt":"A picture"},"children":[]}]}]}}""";
}
