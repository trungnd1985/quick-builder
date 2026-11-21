using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 022 SC-003, research PV-22: a preview of a 50-block page with 30 images appears in under
/// 500 ms at p95, for members and for link viewers, and its images are served as fast.
/// </summary>
/// <remarks>
/// Measured as <see cref="RenderPerformanceTests"/> measures, against the built image on the
/// reference compose setup. A preview is never cached, so every request is a full render plus the
/// link's checks (PV-08). The images are fetched through the link, which checks each against what
/// the page uses (PV-11).
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class PreviewBudgetTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "preview.perf.test";
    private const int Runs = 200;
    private const int ImageRuns = 20;
    private const int Images = 30;
    private const int TextBlocks = 20;

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);

    private readonly ComposeDeployment _deployment = new("docker-compose.perf.yml");

    public async Task InitializeAsync()
    {
        if (!await _deployment.StartAsync().ConfigureAwait(false))
        {
            output.WriteLine("Docker is not available; performance test will skip.");
        }
    }

    public Task DisposeAsync() => _deployment.StopAsync();

    [SkippableFact]
    public async Task Member_and_link_previews_and_their_images_are_within_budget()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await PerfApi.ClaimAsync(client, _deployment, "preview-perf@smoke.test").ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Preview perf", hostNames = new[] { Host } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();

        var images = new List<Guid>();
        for (var i = 0; i < Images; i++)
        {
            images.Add(await UploadAsync(client, siteId).ConfigureAwait(true));
        }

        using var created = await client
            .PostAsJsonAsync(new Uri($"/api/v1/pages/sites/{siteId}/pages", UriKind.Relative), new { title = "Preview", slug = "preview" })
            .ConfigureAwait(true);
        var pageId = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PerfApi.SaveAsync(client, $"/api/v1/pages/sites/{siteId}/pages/{pageId}", Document(images)).ConfigureAwait(true);

        using var linked = await client.PostAsJsonAsync(
                new Uri($"/api/v1/editor/sites/{siteId}/preview-links", UriKind.Relative),
                new { documentKind = "page", documentId = pageId, expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var linkPath = new Uri((await linked.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("url").GetString()!).AbsolutePath;
        var memberPath = $"/_preview/sites/{siteId}/pages/{pageId}";

        // Warm the process once: the first request pays for JIT, not for rendering.
        var html = await GetAsync(client, memberPath, member: true).ConfigureAwait(true);
        await GetAsync(client, linkPath, member: false).ConfigureAwait(true);

        var members = await TimeAsync(Runs, () => GetAsync(client, memberPath, member: true)).ConfigureAwait(true);
        var links = await TimeAsync(Runs, () => GetAsync(client, linkPath, member: false)).ConfigureAwait(true);

        var mediaPaths = images.Select(id => html.IndexOf($"/media/{id:N}", StringComparison.Ordinal)).ToList();
        Assert.All(mediaPaths, index => Assert.True(index > 0));
        var imageFiles = images.Select(id => ImageFile(html, id)).ToList();
        var imageTimes = new List<TimeSpan>();
        for (var run = 0; run < ImageRuns; run++)
        {
            foreach (var file in imageFiles)
            {
                var timer = Stopwatch.StartNew();
                await GetAsync(client, $"{linkPath}/media/{file}", member: false, html: false).ConfigureAwait(true);
                imageTimes.Add(timer.Elapsed);
            }
        }

        output.WriteLine(
            $"50 blocks, {Images} images, {html.Length} bytes: member p50 {PerfApi.Ms(PerfApi.P(members, 0.5))} p95 {PerfApi.Ms(PerfApi.P(members, 0.95))}; " +
            $"link p50 {PerfApi.Ms(PerfApi.P(links, 0.5))} p95 {PerfApi.Ms(PerfApi.P(links, 0.95))}; " +
            $"image p50 {PerfApi.Ms(PerfApi.P(imageTimes, 0.5))} p95 {PerfApi.Ms(PerfApi.P(imageTimes, 0.95))}");

        Assert.True(PerfApi.P(members, 0.95) <= Budget, $"SC-003: a member preview took {PerfApi.Ms(PerfApi.P(members, 0.95))} at p95.");
        Assert.True(PerfApi.P(links, 0.95) <= Budget, $"SC-003: a link preview took {PerfApi.Ms(PerfApi.P(links, 0.95))} at p95.");
        Assert.True(PerfApi.P(imageTimes, 0.95) <= Budget, $"PV-22: a preview image took {PerfApi.Ms(PerfApi.P(imageTimes, 0.95))} at p95.");
    }

    /// <summary>20 headings, 20 texts, 30 images and 10 sections: about 50 content blocks.</summary>
    private static string Document(IReadOnlyList<Guid> images)
    {
        var blocks = new List<string>();
        for (var i = 0; i < TextBlocks; i++)
        {
            blocks.Add($$"""{"id":"h{{i}}","type":"heading","props":{"text":"Heading {{i}}","level":2},"children":[]}""");
            blocks.Add($$"""{"id":"t{{i}}","type":"text","props":{"text":"Paragraph {{i}} of the preview performance page."},"children":[]}""");
        }

        blocks.AddRange(images.Select((id, i) => $$$"""{"id":"i{{{i}}}","type":"image","props":{"media":{"mediaId":"{{{id}}}"}},"children":[]}"""));

        var sections = blocks.Chunk(7).Select((chunk, i) => $$$"""{"id":"s{{{i}}}","type":"section","props":{},"children":[{{{string.Join(',', chunk)}}}]}""");
        return $$$"""{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{{{string.Join(',', sections)}}}]}}""";
    }

    /// <summary>The address part after <c>/media/</c> that the preview wrote for <paramref name="id"/>.</summary>
    private static string ImageFile(string html, Guid id)
    {
        var start = html.IndexOf($"/media/{id:N}", StringComparison.Ordinal) + "/media/".Length;
        var end = html.IndexOfAny(['"', ' ', ','], start);
        return html[start..end];
    }

    private static async Task<List<TimeSpan>> TimeAsync(int runs, Func<Task<string>> request)
    {
        var times = new List<TimeSpan>(runs);
        for (var run = 0; run < runs; run++)
        {
            var timer = Stopwatch.StartNew();
            await request().ConfigureAwait(false);
            times.Add(timer.Elapsed);
        }

        return times;
    }

    private static async Task<string> GetAsync(HttpClient client, string path, bool member, bool html = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (!member)
        {
            // A reviewer without the session cookie, on the site's own host name.
            request.Headers.Host = Host;
            request.Headers.Add("Cookie", string.Empty);
        }

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}.");
        return html ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : string.Empty;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid siteId)
    {
        var bytes = new byte[2_048];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/media/sites/{siteId}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Headers.Add("X-QuickBuilder-File-Name", "preview.png");

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
    }
}
