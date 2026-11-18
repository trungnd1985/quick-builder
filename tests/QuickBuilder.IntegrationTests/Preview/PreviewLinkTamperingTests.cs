using System.Net;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Features.Preview;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// SC-005: of at least 1,000 altered, truncated, extended, revoked and cross-content links, none
/// shows anything; every answer is the same "no longer available" page, byte for byte (022
/// FR-006, FR-007, FR-010).
/// </summary>
/// <remarks>
/// Requests come from 80 client addresses, each well under the guessing limit (research PV-18),
/// so the throttle never answers instead; <c>PreviewLinkThrottleTests</c> tests it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkTamperingTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() =>
        _world = await TemplatesWorld.CreateAsync(database, "preview-tamper").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task No_altered_link_shows_anything()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(1);
        var page = await _world.PageAsync("secret-plans").ConfigureAwait(true);
        var image = await MediaApi.UploadOkAsync(_world.Admin, _world.Site).ConfigureAwait(true);
        var other = await _world.PageAsync("other").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("Top secret")).ConfigureAwait(true);
        await PublishingApi.SaveAsync(
                _world.Admin,
                PagesApi.PagePath(_world.Site, other),
                """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":["""
                + $$$"""{"id":"i1","type":"image","props":{"media":{"mediaId":"{{{image.Id}}}"}},"children":[]}"""
                + "]}]}}")
            .ConfigureAwait(true);

        var valid = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, expires).ConfigureAwait(true);
        var revoked = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, expires).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, (await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, revoked.Id).ConfigureAwait(true)).StatusCode);

        var secret = valid.Path["/_preview/l/".Length..];
        var paths = new List<string>();

        // Every position changed to every other character: 43 × 63.
        for (var i = 0; i < secret.Length; i++)
        {
            foreach (var c in Alphabet.Where(c => c != secret[i]))
            {
                paths.Add($"/_preview/l/{secret[..i]}{c}{secret[(i + 1)..]}");
            }
        }

        // Truncated, extended, empty, blank, revoked, traversing, and another page's media.
        paths.AddRange(Enumerable.Range(0, secret.Length).Select(n => $"/_preview/l/{secret[..n]}"));
        paths.AddRange(Enumerable.Range(1, 10).Select(n => $"/_preview/l/{secret}{new string('A', n)}"));
        paths.AddRange(["/_preview/l/", "/_preview/l/%20", "/_preview/l/%20%20%20", revoked.Path, $"{revoked.Path}/media/x.png"]);
        paths.AddRange([$"/_preview/l/{secret}/../x", $"/_preview/l/{secret}/media/../../sites", $"/_preview/l/{secret}/media/{image.PublicPath["/_media/".Length..]}"]);
        paths.AddRange([$"/_preview/l/{secret}/media/", $"/_preview/l/{secret}/media/nothing.png", $"/_preview/sites/{_world.Site}/pages/{page}"]);

        Assert.True(paths.Count >= 1_000, $"Only {paths.Count} variants.");

        var expected = NotAvailablePage.Gone.Body.ToArray();
        var refusals = 0;
        for (var i = 0; i < paths.Count; i++)
        {
            using var client = _world.App.CreateClientForHost(CmsScenario.HostA);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(paths[i], UriKind.Relative));
            request.Headers.Add(IdentitySeed.RemoteAddressHeader, $"198.51.{i % 80}.{10 + (i % 7)}");
            using var response = await client.SendAsync(request).ConfigureAwait(true);
            var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true);

            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{paths[i]} answered {(int)response.StatusCode}.");
            Assert.True(expected.AsSpan().SequenceEqual(body), $"{paths[i]} answered another body.");
            refusals++;
        }

        Assert.Equal(paths.Count, refusals);

        // The real link still works.
        Assert.Contains(">Top secret</h1>", (await PreviewLinksApi.OpenAsync(_world.App, valid.Path, CmsScenario.HostA).ConfigureAwait(true)).Body, StringComparison.Ordinal);
    }
}
