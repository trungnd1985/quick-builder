using System.Net;
using Microsoft.AspNetCore.Http;
using QuickBuilder.Host.Routing;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// The <c>/_preview</c> prefix (022 research PV-05, public-preview.md §1): answered on every host
/// name with the site taken from the link or route, never from <c>Host</c>, and never logged.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewSurfaceTests(DatabaseFixture database) : IAsyncLifetime
{
    // Well-formed (43 base64url characters) but readable, so secret scanners do not flag it.
    private const string Secret = "not-a-real-link-secret-only-for-these-tests";
    private const string SiteNotResolved = "sites.not_resolved";

    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "preview-surface").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData(PagesScenario.HostA)]
    [InlineData("nobody.example")]
    public async Task Previews_answer_on_every_host_name_without_resolving_a_site(string host)
    {
        var response = await _scenario.App.CreateClientForHost(host)
            .GetAsync(new Uri($"/_preview/l/{Secret}", UriKind.Relative)).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(SiteNotResolved, body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prefix_is_not_administrative_so_its_paths_are_never_logged()
    {
        Assert.Equal("/_preview", RoutePrefixes.Preview);
        Assert.Contains(RoutePrefixes.Preview, RoutePrefixes.UnresolvedPublicPrefixes);
        Assert.False(RoutePrefixes.IsReserved(new PathString($"/_preview/l/{Secret}")));
    }

    [Fact]
    public async Task A_link_secret_never_reaches_the_logs()
    {
        await _scenario.App.CreateClientForHost(PagesScenario.HostA)
            .GetAsync(new Uri($"/_preview/l/{Secret}", UriKind.Relative)).ConfigureAwait(true);

        foreach (var logEvent in _scenario.App.Logs.Events)
        {
            Assert.DoesNotContain(Secret, LogCapture.Render(logEvent), StringComparison.Ordinal);
            if (logEvent.Properties.TryGetValue("RequestPath", out var path))
            {
                Assert.DoesNotContain("/_preview", path.ToString(), StringComparison.Ordinal);
            }
        }
    }
}
