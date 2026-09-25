using System.Net;
using System.Net.Http.Headers;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// SC-004: no uploaded file is ever served in a way a browser could treat as a page or run
/// as script on the site (003 FR-016, research M-11, M-12).
/// </summary>
/// <remarks>
/// Every accepted file from the SC-002 crafted set — including the polyglots that lead with
/// an allowed signature — plus the six samples, on both the public address and the preview.
/// For PDFs this checks what the server controls; the browser behaviour is the manual merge
/// gate in quickstart §4 (plan Risks).
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MediaServingHeadersTests(DatabaseFixture database) : IAsyncLifetime
{
    private MediaScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await MediaScenario.CreateAsync(database, "serving-headers").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Every_accepted_file_is_served_inert_on_both_surfaces()
    {
        var cases = CraftedFiles.Accepted
            .Where(c => c.Bytes.Length <= 1_000_000)
            .Select(c => (c.Name, c.Bytes, c.FileName!, c.ContentType))
            .Concat(SampleFiles.All.Select(s => ($"sample {s.Extension}", s.Bytes, $"sample.{s.Extension}", (string?)s.ContentType)))
            .ToList();

        var failures = new List<string>();

        foreach (var (name, bytes, fileName, declared) in cases)
        {
            var upload = await MediaApi.UploadAsync(_scenario.Admin, _scenario.SiteA, bytes, fileName, declared).ConfigureAwait(true);
            if (upload.StatusCode != HttpStatusCode.Created)
            {
                failures.Add($"{name}: upload answered {(int)upload.StatusCode}");
                continue;
            }

            var file = System.Text.Json.JsonSerializer.Deserialize<QuickBuilder.Media.Contracts.MediaFileDto>(
                await upload.Content.ReadAsStringAsync().ConfigureAwait(true), MediaApi.Json)!;

            var served = await MediaApi.PublicAsync(_scenario.App, MediaScenario.HostA, file.PublicPath).ConfigureAwait(true);
            var preview = await MediaApi.PreviewAsync(_scenario.Admin, _scenario.SiteA, file.Id).ConfigureAwait(true);

            failures.AddRange(Check($"{name} (public)", served, file.ContentType, isPreview: false));
            failures.AddRange(Check($"{name} (preview)", preview, file.ContentType, isPreview: true));
        }

        Assert.True(cases.Count >= 20, $"Only {cases.Count} files were checked.");
        Assert.True(failures.Count == 0, "Served without the required safety headers:\n  " + string.Join("\n  ", failures));
    }

    private static IEnumerable<string> Check(string name, HttpResponseMessage response, string contentType, bool isPreview)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            yield return $"{name}: answered {(int)response.StatusCode}";
            yield break;
        }

        if (response.Content.Headers.ContentType?.MediaType != contentType)
        {
            yield return $"{name}: Content-Type {response.Content.Headers.ContentType} instead of {contentType}";
        }

        if (Header(response, "X-Content-Type-Options") != "nosniff")
        {
            yield return $"{name}: missing X-Content-Type-Options: nosniff";
        }

        var disposition = response.Content.Headers.ContentDisposition;
        if (disposition?.DispositionType != "inline" || disposition.FileNameStar is null)
        {
            yield return $"{name}: Content-Disposition is '{disposition}', expected inline with filename*";
        }

        var csp = Header(response, "Content-Security-Policy");
        if (contentType.StartsWith("image/", StringComparison.Ordinal) && csp != MediaResponseHeaders.ImageContentSecurityPolicy)
        {
            yield return $"{name}: image served with CSP '{csp}'";
        }

        if (contentType == "application/pdf" && csp is not null)
        {
            yield return $"{name}: PDF served with a CSP, which stops browsers rendering it";
        }

        var cache = response.Headers.CacheControl;
        var cacheOk = isPreview
            ? cache is { Private: true, NoCache: true, Public: false }
            : cache is { Public: true, MaxAge.TotalDays: >= 365 } && cache.Extensions.Any(e => e.Name == "immutable");
        if (!cacheOk)
        {
            yield return $"{name}: Cache-Control '{cache}'";
        }

        if (response.Headers.ETag is not { IsWeak: false })
        {
            yield return $"{name}: missing a strong ETag";
        }

        if (response.Content.Headers.LastModified is null)
        {
            yield return $"{name}: missing Last-Modified";
        }

        if (!response.Headers.AcceptRanges.Contains("bytes"))
        {
            yield return $"{name}: missing Accept-Ranges: bytes";
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : null;
}
