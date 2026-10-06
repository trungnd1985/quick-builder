using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Contracts;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// User Story 2: visitors receive files from the site's own address, exactly as uploaded
/// (003 FR-013, FR-017, US2 scenarios 1 and 5).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublicServingTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "public-serving").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Each_type_is_served_byte_for_byte_on_its_sites_host_without_signing_in()
    {
        foreach (var (bytes, extension, contentType) in SampleFiles.All)
        {
            var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, bytes, extension, contentType).ConfigureAwait(true);

            var response = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(bytes.Length, response.Content.Headers.ContentLength);
            Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task Head_returns_the_same_headers_and_no_body()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Png).ConfigureAwait(true);

        var get = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true);
        var head = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath, HttpMethod.Head).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
        Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
        Assert.Equal(get.Headers.ETag, head.Headers.ETag);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    /// <summary>US2 scenario 5: partial and conditional requests (seeking in a PDF, resuming a download).</summary>
    [Fact]
    public async Task Range_and_conditional_requests_are_honoured()
    {
        var bytes = SampleFiles.OfSize(SampleFiles.Pdf, 5_000);
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, bytes, "pdf", "application/pdf").ConfigureAwait(true);

        var partial = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath,
            configure: r => r.Headers.Range = new RangeHeaderValue(0, 99)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("bytes 0-99/5000", partial.Content.Headers.ContentRange?.ToString());
        Assert.Equal(bytes.AsSpan(0, 100).ToArray(), await partial.Content.ReadAsByteArrayAsync().ConfigureAwait(true));

        var beyond = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath,
            configure: r => r.Headers.Range = new RangeHeaderValue(10_000, 10_100)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, beyond.StatusCode);

        var first = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true);
        var unchanged = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath,
            configure: r => r.Headers.IfNoneMatch.Add(first.Headers.ETag!)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
    }

    /// <summary>
    /// 013 research MV-08: a version-1 file keeps its 003 address and ETag, and only the
    /// canonical form of an address is served.
    /// </summary>
    [Fact]
    public async Task Only_canonical_addresses_serve_and_version_one_keeps_the_003_form()
    {
        var file = await MediaApi.UploadOkAsync(_scenario.Admin, _scenario.SiteA, SampleFiles.Png).ConfigureAwait(true);
        var id = file.Id.ToString("N");
        Assert.EndsWith($"/_media/{id}.png", file.PublicPath, StringComparison.Ordinal);

        var served = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal($"\"{id}\"", served.Headers.ETag!.Tag);

        foreach (var other in new[]
        {
            $"/_media/{id}-v1.png",          // version 1's original has one address
            $"/_media/{id}-v2.png",          // not a version that exists
            $"/_media/{id}-v1-w320.png",     // no variants yet (US2), and a PNG this small has none
            $"/_media/{id}-v1-w700.png",     // not a configured width
            $"/_media/{id}-v01.png",
            $"/_media/{id}.PNG",
            $"/_media/{id}.jpg",
        })
        {
            var response = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, other).ConfigureAwait(true);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{other} answered {response.StatusCode}");
        }
    }

    /// <summary>FR-013: public addresses include the configured base path.</summary>
    [Fact]
    public async Task With_a_base_path_the_address_includes_it_and_is_served_under_it()
    {
        using var app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("public-base-path").ConfigureAwait(true),
            settings: new Dictionary<string, string> { ["Hosting:BasePath"] = "/cms" });
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await MediaApi.CreateSiteAsync(admin, "Based", "based.test").ConfigureAwait(true);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/cms{MediaApi.FilesPath(siteId)}", UriKind.Relative))
        {
            Content = new ByteArrayContent(SampleFiles.Png),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Headers.Add("X-QuickBuilder-File-Name", "based.png");
        var upload = await admin.SendAsync(request).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var file = JsonSerializer.Deserialize<MediaFileDto>(await upload.Content.ReadAsStringAsync().ConfigureAwait(true), MediaApi.Json)!;

        Assert.StartsWith("/cms/_media/", file.PublicPath, StringComparison.Ordinal);

        var served = await MediaApi.PublicAsync(app, "based.test", file.PublicPath).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(SampleFiles.Png, await served.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }
}
