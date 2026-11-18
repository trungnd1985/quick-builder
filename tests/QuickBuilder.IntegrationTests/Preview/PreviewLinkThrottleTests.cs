using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// Guessing at links is slowed down (022 FR-013, research PV-18): past 20 unknown links a minute
/// from one address group, its link requests wait, valid ones too; other addresses do not.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkThrottleTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Prober = "203.0.113.50";

    private readonly ManualClock _clock = new();
    private TemplatesWorld _world = null!;
    private CreatedLink _valid = null!;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "preview-throttle", s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock)))
            .ConfigureAwait(false);
        var page = await _world.PageAsync("about").ConfigureAwait(false);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("About")).ConfigureAwait(false);
        _valid = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, _clock.GetUtcNow().AddDays(2)).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> OpenAsync(string path, string address = Prober) =>
        PreviewLinksApi.OpenAsync(_world.App, path, CmsScenario.HostA, address);

    private static string Unknown(int i) => "/_preview/l/" + i.ToString("D43", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Twenty_unknown_links_a_minute_are_answered_and_the_next_waits_valid_ones_too()
    {
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(Unknown(i)).ConfigureAwait(true)).Status);
        }

        var (status, _, response) = await OpenAsync(Unknown(20)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.True(int.Parse(response.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) is > 0 and <= 60);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await OpenAsync(_valid.Path).ConfigureAwait(true)).Status);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(_valid.Path, "198.51.100.99").ConfigureAwait(true)).Status);

        // The window ends: the prober is answered again.
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(_valid.Path).ConfigureAwait(true)).Status);
    }

    [Fact]
    public async Task Expired_and_revoked_links_do_not_count_and_only_hashed_keys_are_stored()
    {
        var page = await _world.PageAsync("revoked").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("Revoked")).ConfigureAwait(true);
        var revoked = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);
        await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, revoked.Id).ConfigureAwait(true);

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(revoked.Path).ConfigureAwait(true)).Status);
        }

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(_valid.Path).ConfigureAwait(true)).Status);

        await OpenAsync(Unknown(1)).ConfigureAwait(true);
        await using var scope = _world.App.Services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<EditorDbContext>().PreviewLinkFailures
            .Select(w => w.ClientKey)
            .ToListAsync()
            .ConfigureAwait(true);
        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.Equal(32, key.Length));
        Assert.Contains(keys, key => key.AsSpan().SequenceEqual(PreviewLinkFailures.Key(System.Net.IPAddress.Parse(Prober))));
    }
}
