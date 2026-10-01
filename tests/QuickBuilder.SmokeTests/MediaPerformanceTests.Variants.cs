using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.TestFixtures.Media;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 013 SC-004 (tasks T070): variants on the reference environment. Part of the media
/// performance test so it shares that test's deployment, site and in-network client.
/// </summary>
public sealed partial class MediaPerformanceTests
{
    private static readonly TimeSpan FirstVariantTarget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// SC-004: the first request for each width of a 20 MiB JPEG and a large AVIF completes
    /// within 3 s, and a stored variant's first byte is within 100 ms at p95 with 50 at once
    /// over 1,000 requests.
    /// </summary>
    private async Task<List<string>> MeasureVariantsAsync(HttpClient client, string network, string session, Guid siteId)
    {
        var jpeg = await UploadImageAsync(client, session, siteId, ImageFixtures.TwentyMebibytePhoto, "image/jpeg", "photo-20mib.jpg").ConfigureAwait(true);
        var avif = await UploadImageAsync(client, session, siteId, ImageFixtures.LargeAvif, "image/avif", "photo.avif").ConfigureAwait(true);
        output.WriteLine($"variant sources: JPEG {ImageFixtures.TwentyMebibytePhoto.Length:N0} bytes, AVIF {ImageFixtures.LargeAvif.Length:N0} bytes");

        // One at a time, so each measures one production rather than a queue. The smallest
        // width is the eagerly produced thumbnail and may already be stored.
        var firsts = jpeg.Paths.Concat(avif.Paths).ToList();
        var results = await InNetworkAsync(network, $$"""
            for path in {{string.Join(' ', firsts)}}; do
              echo "first $path $(curl -s -o /dev/null -H 'Host: {{Host}}' -w '%{http_code} %{time_total} %header{cache-control}' "http://app:8080$path")"
            done
            """).ConfigureAwait(true);

        var misses = new List<string>();
        foreach (var line in results.Where(r => r[0] == "first"))
        {
            var (path, status, time, cache) = (line[1], line[2], Seconds(line[3]), string.Join(' ', line.Skip(4)));
            output.WriteLine($"first request {path}: {status} {Ms(time)} {cache}");
            if (status != "200" || cache.Contains("no-store", StringComparison.Ordinal))
            {
                misses.Add($"SC-004: {path} was not produced ({status}, {cache}).");
            }
            else if (time > FirstVariantTarget)
            {
                misses.Add($"SC-004: the first request for {path} took {Ms(time)}, above {Ms(FirstVariantTarget)}.");
            }
        }

        var stored = jpeg.Paths[jpeg.Paths.Count / 2];
        var serving = await InNetworkAsync(network, $$"""
            url=http://app:8080{{stored}}
            for i in $(seq 5); do curl -s -o /dev/null -H 'Host: {{Host}}' "$url"; done
            seq 1000 | xargs -P 50 -I{} curl -s -o /dev/null -H 'Host: {{Host}}' -w 'ttfb %{http_code} %{time_starttransfer}\n' "$url"
            """).ConfigureAwait(true);

        var samples = serving.Where(r => r[0] == "ttfb").ToList();
        Assert.All(samples, r => Assert.Equal("200", r[1]));
        var times = samples.Select(r => Seconds(r[2])).ToList();
        output.WriteLine($"stored variant {stored}, 50 at once: first byte p50 {Ms(Percentile(times, 0.5))} p95 {Ms(P95(times))} (n={times.Count})");

        if (P95(times) > FirstByteTarget)
        {
            misses.Add($"SC-004: stored-variant first-byte p95 was {Ms(P95(times))}, above {Ms(FirstByteTarget)}.");
        }

        return misses;
    }

    /// <summary>Uploads an image and returns the public address of each of its variants.</summary>
    private static async Task<(Guid Id, List<string> Paths)> UploadImageAsync(
        HttpClient client, string session, Guid siteId, byte[] bytes, string contentType, string fileName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/media/sites/{siteId}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Headers.Add("X-QuickBuilder-File-Name", fileName);
        request.Headers.Add("Cookie", session);

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

        // Version 1 is served at /_media/{id}.{ext}; its variants at /_media/{id}-v1-w{w}.{ext}.
        var publicPath = file.GetProperty("publicPath").GetString()!;
        var dot = publicPath.LastIndexOf('.');
        var paths = file.GetProperty("variants").EnumerateArray()
            .Select(v => $"{publicPath[..dot]}-v1-w{v.GetProperty("width").GetInt32()}{publicPath[dot..]}")
            .ToList();
        Assert.NotEmpty(paths);

        return (file.GetProperty("id").GetGuid(), paths);
    }
}
