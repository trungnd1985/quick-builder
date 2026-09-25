using System.Net;
using System.Net.Http.Headers;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Site members preview files through an authenticated operation that never depends on a
/// site host name, under the same safety rules as public serving (003 FR-018, research M-12).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;
    private Guid _unvisitedSite;

    public async Task InitializeAsync()
    {
        _scenario = await MediaScenario.CreateAsync(database, "preview").ConfigureAwait(false);
        // Sites requires a host name at creation (001), so "no host yet" cannot be set up; a
        // host nobody requests shows the same thing: preview never goes through it.
        _unvisitedSite = await MediaApi.CreateSiteAsync(_scenario.Admin, "Unvisited", "never-requested.test").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_file_is_previewed_exactly_on_the_admin_surface_without_its_sites_host()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _unvisitedSite, SampleFiles.Png).ConfigureAwait(true);

        var response = await MediaApi.PreviewAsync(_scenario.Admin, _unvisitedSite, file.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SampleFiles.Png, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_preview_carries_the_safety_headers_and_revalidates()
    {
        var image = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Gif, "gif", "image/gif").ConfigureAwait(true);
        var pdf = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Pdf, "pdf", "application/pdf").ConfigureAwait(true);

        var imageResponse = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, image.Id).ConfigureAwait(true);
        var pdfResponse = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, pdf.Id).ConfigureAwait(true);

        Assert.Equal("nosniff", Single(imageResponse, "X-Content-Type-Options"));
        Assert.Equal(MediaResponseHeaders.ImageContentSecurityPolicy, Single(imageResponse, "Content-Security-Policy"));
        // Revalidate every view (re-authorized), never shared: "private, no-cache".
        Assert.True(imageResponse.Headers.CacheControl is { Private: true, NoCache: true, Public: false });
        Assert.Equal("inline", imageResponse.Content.Headers.ContentDisposition?.DispositionType);

        Assert.Equal("nosniff", Single(pdfResponse, "X-Content-Type-Options"));
        Assert.False(pdfResponse.Headers.Contains("Content-Security-Policy"), "PDFs cannot be sandboxed (research M-11).");
    }

    [Fact]
    public async Task Conditional_and_range_requests_are_honoured()
    {
        var bytes = SampleFiles.OfSize(SampleFiles.Pdf, 10_000);
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, bytes, "pdf", "application/pdf").ConfigureAwait(true);

        var first = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, file.Id).ConfigureAwait(true);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(etag.IsWeak);

        var unchanged = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, file.Id, r => r.Headers.IfNoneMatch.Add(etag)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);

        var partial = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, file.Id, r => r.Headers.Range = new RangeHeaderValue(100, 199)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(bytes.AsSpan(100, 100).ToArray(), await partial.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Another_sites_file_is_not_found_under_this_site()
    {
        var onB = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteB).ConfigureAwait(true);

        var response = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, onB.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("media.not_found", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_made_up_site_is_not_found_even_for_an_administrator()
    {
        var response = await MediaApi.PreviewAsync(_scenario.Admin, Guid.NewGuid(), Guid.NewGuid()).ConfigureAwait(true);
        var siteNotFound = await _scenario.Admin.GetAsync(new Uri($"/api/v1/sites/{Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            await SignInHelpers.BodyWithoutTraceIdAsync(siteNotFound).ConfigureAwait(true),
            await SignInHelpers.BodyWithoutTraceIdAsync(response).ConfigureAwait(true));
    }

    [Fact]
    public async Task Previewing_without_signing_in_is_refused()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var response = await MediaApi.PreviewAsync(_scenario.App.CreateAnonymousClient(), _scenario.SiteA, file.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string? Single(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? values.Single() : null;
}
