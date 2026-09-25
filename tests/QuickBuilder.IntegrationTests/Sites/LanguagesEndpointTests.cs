using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>The supported-language list (009 research S-09, FR-003).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class LanguagesEndpointTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync() =>
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("languages").ConfigureAwait(false));

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Any_signed_in_user_gets_the_forty_tags_in_order_with_english_as_default()
    {
        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = (await SitesApi.CreateOkAsync(admin, "Any", ["any.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();
        var editor = await IdentitySeed.CreateSignedInClientAsync(
            _app,
            (await IdentitySeed.SeedUserAsync(_app.Services, siteRoles: [(siteId, SystemRole.Editor)]).ConfigureAwait(true)).Id).ConfigureAwait(true);

        foreach (var client in new[] { admin, editor })
        {
            var response = await SitesApi.LanguagesAsync(client).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await SitesApi.BodyAsync(response).ConfigureAwait(true);
            var tags = body.GetProperty("languages").EnumerateArray().Select(t => t.GetString()).ToList();
            Assert.Equal(40, tags.Count);
            Assert.Equal("ar", tags[0]);
            Assert.Contains("pt-BR", tags);
            Assert.Equal("en", body.GetProperty("default").GetString());
        }
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        // Out of setup mode, where every admin call answers 428 before authentication.
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(true);

        var response = await SitesApi.LanguagesAsync(_app.CreateAnonymousClient()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
