using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// User Story 3: administrators manage accounts (spec FR-010, FR-010a, FR-011; US3
/// scenarios 1, 4, 5, 6, 9, 10).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UserManagementTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("users").ConfigureAwait(false));
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US3 scenario 1: a new account has no password, and the administrator gets a link.</summary>
    [Fact]
    public async Task Creating_a_user_returns_a_one_time_link_and_no_password()
    {
        var response = await UsersApi.CreateAsync(_admin, "newcomer@example.test").ConfigureAwait(true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(body.GetProperty("user").GetProperty("hasPassword").GetBoolean());
        Assert.Equal("active", body.GetProperty("user").GetProperty("status").GetString());

        var link = body.GetProperty("setPasswordLink");
        Assert.False(string.IsNullOrEmpty(link.GetProperty("token").GetString()));
        var expires = link.GetProperty("expiresAtUtc").GetDateTimeOffset();
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromHours(23), TimeSpan.FromHours(25));

        Assert.Equal(1, await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.AccountCreated).ConfigureAwait(true));
    }

    /// <summary>US3 scenario 4: email addresses are unique whatever their case.</summary>
    [Fact]
    public async Task A_taken_email_address_is_refused_in_any_case()
    {
        await UsersApi.CreateUserAsync(_admin, "taken@example.test").ConfigureAwait(true);

        var again = await UsersApi.CreateAsync(_admin, "  TAKEN@Example.test ").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("identity.email_taken", await UsersApi.ErrorCodeAsync(again).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_user_can_be_renamed()
    {
        var (id, _) = await UsersApi.CreateUserAsync(_admin, "rename@example.test").ConfigureAwait(true);

        var response = await UsersApi.UpdateAsync(_admin, id, new { displayName = "  Renamed Person " }).ConfigureAwait(true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed Person", body.GetProperty("displayName").GetString());
    }

    /// <summary>
    /// US3 scenario 10, FR-010a: an email change signs the user out everywhere, voids the
    /// live link, and records both addresses.
    /// </summary>
    [Fact]
    public async Task Changing_an_email_signs_the_user_out_and_voids_their_link()
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services, email: "before@example.test").ConfigureAwait(true);
        var session = await IdentitySeed.CreateSignedInClientAsync(_app, user.Id).ConfigureAwait(true);
        var link = await UsersApi.IssueLinkTokenAsync(_admin, user.Id).ConfigureAwait(true);

        var response = await UsersApi.UpdateAsync(_admin, user.Id, new { email = "after@example.test" }).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(session).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await UsersApi.CheckLinkAsync(_app.CreateAnonymousClient(), link).ConfigureAwait(true)).StatusCode);

        var browser = SignInHelpers.Browser(_app, "192.0.2.90");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(browser, "before@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.91"), "after@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var details = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .SecurityEvents.Where(e => e.EventType == SecurityEventTypes.EmailChanged && e.SubjectUserId == user.Id)
            .Select(e => e.Details).SingleAsync().ConfigureAwait(true);
        Assert.Contains("before@example.test", details, StringComparison.Ordinal);
        Assert.Contains("after@example.test", details, StringComparison.Ordinal);
    }

    /// <summary>US3 scenario 6: deactivation ends access at once; reactivation restores it.</summary>
    [Fact]
    public async Task Deactivation_ends_access_and_reactivation_restores_it()
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services, email: "worker@example.test").ConfigureAwait(true);
        var session = await IdentitySeed.CreateSignedInClientAsync(_app, user.Id).ConfigureAwait(true);

        var deactivated = await UsersApi.DeactivateAsync(_admin, user.Id).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal("deactivated", (await deactivated.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(session).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.92"), "worker@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await UsersApi.ReactivateAsync(_admin, user.Id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.93"), "worker@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode);

        // The old session stays dead: reactivation restores the password, not past sessions.
        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(session).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>US3 scenario 9: only administrators manage users — even their own email.</summary>
    [Fact]
    public async Task A_non_administrator_is_refused_every_user_operation()
    {
        var siteId = await UsersApi.CreateSiteAsync(_admin, "editors-site.test").ConfigureAwait(true);
        var editor = await IdentitySeed.SeedUserAsync(_app.Services, siteRoles: [(siteId, SystemRole.Editor)]).ConfigureAwait(true);
        var client = await IdentitySeed.CreateSignedInClientAsync(_app, editor.Id).ConfigureAwait(true);

        var calls = new List<Func<Task<HttpResponseMessage>>>
        {
            () => client.GetAsync(new Uri("/api/v1/identity/users", UriKind.Relative)),
            () => client.GetAsync(new Uri($"/api/v1/identity/users/{editor.Id}", UriKind.Relative)),
            () => UsersApi.CreateAsync(client, "sneaky@example.test"),
            () => UsersApi.UpdateAsync(client, editor.Id, new { email = "mine@example.test" }),
            () => UsersApi.ReplaceGrantsAsync(client, editor.Id, UsersApi.Grants(administrator: true)),
            () => UsersApi.DeactivateAsync(client, editor.Id),
            () => UsersApi.ReactivateAsync(client, editor.Id),
            () => UsersApi.IssueLinkAsync(client, editor.Id),
            () => UsersApi.RevokeSessionsAsync(client, editor.Id),
        };

        foreach (var call in calls)
        {
            var response = await call().ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("identity.forbidden", await UsersApi.ErrorCodeAsync(response).ConfigureAwait(true));
        }
    }

    /// <summary>Spec edge case: simultaneous name edits — the last write wins, whole.</summary>
    [Fact]
    public async Task Simultaneous_edits_resolve_to_one_of_them()
    {
        var (id, _) = await UsersApi.CreateUserAsync(_admin, "contested@example.test").ConfigureAwait(true);

        var results = await Task.WhenAll(
            UsersApi.UpdateAsync(_admin, id, new { displayName = "Name A" }),
            UsersApi.UpdateAsync(_admin, id, new { displayName = "Name B" })).ConfigureAwait(true);

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var stored = await _admin.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/identity/users/{id}", UriKind.Relative)).ConfigureAwait(true);
        Assert.Contains(stored.GetProperty("displayName").GetString(), new[] { "Name A", "Name B" });
    }

    [Fact]
    public async Task Users_are_listed_with_their_grants()
    {
        var siteId = await UsersApi.CreateSiteAsync(_admin, "listed.test").ConfigureAwait(true);
        var (id, _) = await UsersApi.CreateUserAsync(_admin, "listed@example.test", UsersApi.Grants(false, (siteId, "designer"))).ConfigureAwait(true);

        var users = await _admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/users", UriKind.Relative)).ConfigureAwait(true);
        var listed = users.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);
        var role = listed.GetProperty("grants").GetProperty("siteRoles").EnumerateArray().Single();

        Assert.Equal(siteId, role.GetProperty("siteId").GetGuid());
        Assert.Equal("designer", role.GetProperty("role").GetString());
        Assert.False(listed.GetProperty("grants").GetProperty("administrator").GetBoolean());
    }
}
