using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetVips;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 2: variants served on the site's host, produced once and then read from storage,
/// only at widths the file has, falling back safely, and bounded (013 FR-021 – FR-025, FR-029,
/// contracts/public-media.md §1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class VariantServingTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(
            database,
            "variant-serving",
            services => ControllableEncoder.Install(services),
            settings: new Dictionary<string, string>
            {
                ["Media:Variants:MaxConcurrent"] = "1",
                ["Media:Variants:Timeout"] = "00:00:02",
            }).ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private ControllableEncoder Encoder => ControllableEncoder.Of(_scenario.App.Services);

    private Task<MediaFileDto> UploadPhotoAsync(Guid? site = null) =>
        LibraryApi.UploadNamedAsync(_scenario.Admin, site ?? _scenario.SiteA, "photo.jpg", ImageFixtures.LargePhoto, "image/jpeg");

    private static string Variant(MediaFileDto file, int width) => $"/_media/{file.Id:N}-v{file.ContentVersion}-w{width}.jpg";

    [Fact]
    public async Task A_variant_is_served_with_the_originals_headers_and_then_from_storage()
    {
        var file = await UploadPhotoAsync().ConfigureAwait(true);
        Assert.Equal((6_000, 4_000), (file.Width, file.Height));

        var first = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, Variant(file, 640)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/jpeg", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", first.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", first.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal($"\"{file.Id:N}-v1-w640\"", first.Headers.ETag!.Tag);
        Assert.Contains("immutable", first.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        using (var image = Image.NewFromBuffer(await first.Content.ReadAsByteArrayAsync().ConfigureAwait(true)))
        {
            Assert.Equal(640, image.Width);
        }

        var productions = Encoder.ByWidth.GetValueOrDefault(640);
        var second = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, Variant(file, 640)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(productions, Encoder.ByWidth.GetValueOrDefault(640));

        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostB, Variant(file, 640)).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Widths_the_file_does_not_have_and_files_without_variants_are_not_found()
    {
        var photo = await UploadPhotoAsync().ConfigureAwait(true);
        var logo = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "logo.png", ImageFixtures.Logo, "image/png").ConfigureAwait(true);
        var gif = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "anim.gif", ImageFixtures.AnimatedGif, "image/gif").ConfigureAwait(true);
        var pdf = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "doc.pdf", SampleFiles.Pdf, "application/pdf").ConfigureAwait(true);

        foreach (var path in new[]
        {
            $"/_media/{photo.Id:N}-v1-w700.jpg",   // not configured
            $"/_media/{photo.Id:N}-v2-w640.jpg",   // not its version
            $"/_media/{logo.Id:N}-v1-w960.png",    // wider than the 800-pixel logo
            $"/_media/{gif.Id:N}-v1-w320.gif",     // GIFs have none
            $"/_media/{pdf.Id:N}-v1-w320.pdf",     // documents have none
        })
        {
            var response = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, path).ConfigureAwait(true);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path} answered {response.StatusCode}");
        }

        Assert.Equal([320, 640], logo.Variants.Select(v => v.Width));
        Assert.Empty(gif.Variants);
        Assert.Empty(pdf.Variants);
    }

    [Fact]
    public async Task The_admin_variant_is_served_on_the_admin_surface_and_needs_the_view_capability()
    {
        // Through the admin route, not the site's host: what makes it work before a site is reachable.
        var file = await UploadPhotoAsync().ConfigureAwait(true);
        var path = file.Variants.Single(v => v.Width == 320).Path;

        var response = await _scenario.Admin.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("private", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal(path, file.ThumbnailPath);

        var stale = path.Replace("?v=1", "?v=2", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await _scenario.Admin.GetAsync(new Uri(stale, UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        var (outsider, _) = await _scenario.UserAsync((_scenario.SiteB, QuickBuilder.Shared.Kernel.Authorization.SystemRole.Designer)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_variant_that_cannot_be_produced_sends_the_original_uncached_and_withdraws_the_variants()
    {
        var file = await UploadPhotoAsync().ConfigureAwait(true);
        Encoder.Fail = true;

        var response = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, Variant(file, 960)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(ImageFixtures.LargePhoto.Length, response.Content.Headers.ContentLength);

        var row = (await _scenario.RowsAsync().ConfigureAwait(true)).Single(f => f.Id == file.Id);
        Assert.Equal(MediaVariantsState.Unavailable, row.VariantsState);
        Assert.Equal(1, await OutboxCountAsync(FileChanged.EventType).ConfigureAwait(true));

        Encoder.Fail = false;
        Assert.Equal(HttpStatusCode.NotFound, (await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, Variant(file, 320)).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>FR-029 (analysis M2): one slot, a 2 s timeout, and the admin surface stays responsive.</summary>
    [Fact]
    public async Task Production_is_bounded_and_times_out_to_the_original_without_holding_up_the_admin()
    {
        var first = await UploadPhotoAsync().ConfigureAwait(true);
        var second = await UploadPhotoAsync().ConfigureAwait(true);
        Encoder.Delay = TimeSpan.FromSeconds(5);
        Encoder.IgnoreCancellation = true;

        var slow = MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, Variant(first, 1280));
        await Task.Delay(300).ConfigureAwait(true);
        var waiting = MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, Variant(second, 1280));

        var watch = Stopwatch.StartNew();
        var list = await LibraryApi.ListAsync(_scenario.Admin, _scenario.SiteA, string.Empty).ConfigureAwait(true);
        watch.Stop();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"The admin list took {watch.Elapsed}.");

        var responses = await Task.WhenAll(slow, waiting).ConfigureAwait(true);

        // Both fell back to the original, uncached.
        Assert.All(responses, r => Assert.Equal("no-store", r.Headers.CacheControl!.ToString()));

        var rows = (await _scenario.RowsAsync().ConfigureAwait(true)).ToDictionary(r => r.Id);

        // The first encoded past its timeout: its variants are withdrawn. The second only
        // waited for the slot: that is load, not the image, so its variants stay.
        Assert.Equal(MediaVariantsState.Unavailable, rows[first.Id].VariantsState);
        Assert.Equal(MediaVariantsState.Available, rows[second.Id].VariantsState);
        Assert.Equal(0, Encoder.ByWidth.GetValueOrDefault(1280) - 1);
    }

    private async Task<int> OutboxCountAsync(string eventType)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        return await db.Set<OutboxMessage>().CountAsync(m => m.EventType == eventType).ConfigureAwait(false);
    }
}
