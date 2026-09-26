using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// The installation can never be left without an administrator (spec FR-013; US3 scenario
/// 7; research R-13).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LastAdministratorTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("lastadmin").ConfigureAwait(false));
        _ = _app.Services;
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_only_administrator_cannot_be_deactivated_or_demoted()
    {
        var only = await IdentitySeed.SeedUserAsync(_app.Services, administrator: true).ConfigureAwait(true);
        var client = await IdentitySeed.CreateSignedInClientAsync(_app, only.Id).ConfigureAwait(true);

        var deactivate = await UsersApi.DeactivateAsync(client, only.Id).ConfigureAwait(true);
        var demote = await UsersApi.ReplaceGrantsAsync(client, only.Id, UsersApi.Grants(administrator: false)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal("identity.last_administrator", await UsersApi.ErrorCodeAsync(deactivate).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("identity.last_administrator", await UsersApi.ErrorCodeAsync(demote).ConfigureAwait(true));
        Assert.Equal(1, await UsersApi.ActiveAdministratorCountAsync(_app).ConfigureAwait(true));
    }

    /// <summary>Two administrators demoting each other at once: exactly one survives.</summary>
    [Fact]
    public async Task Racing_demotions_leave_exactly_one_administrator()
    {
        var first = await IdentitySeed.SeedUserAsync(_app.Services, administrator: true).ConfigureAwait(true);
        var second = await IdentitySeed.SeedUserAsync(_app.Services, administrator: true).ConfigureAwait(true);
        var firstClient = await IdentitySeed.CreateSignedInClientAsync(_app, first.Id).ConfigureAwait(true);
        var secondClient = await IdentitySeed.CreateSignedInClientAsync(_app, second.Id).ConfigureAwait(true);

        // Each test gets its own database, so these two are the only administrators.
        Assert.Equal(2, await UsersApi.ActiveAdministratorCountAsync(_app).ConfigureAwait(true));

        var results = await Task.WhenAll(
            UsersApi.ReplaceGrantsAsync(firstClient, second.Id, UsersApi.Grants(administrator: false)),
            UsersApi.ReplaceGrantsAsync(secondClient, first.Id, UsersApi.Grants(administrator: false))).ConfigureAwait(true);

        // The loser is refused one of two ways, depending on timing. If both requests are
        // authorized before either commits, the second trips the last-administrator rule
        // (409). If the winner commits first, the loser is no longer an administrator when
        // its own request is authorized, and is refused as forbidden (403) before the rule
        // is even reached. Both are correct; what must never happen is both succeeding.
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
        Assert.Equal(1, await UsersApi.ActiveAdministratorCountAsync(_app).ConfigureAwait(true));
    }
}
