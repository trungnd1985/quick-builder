using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Grants are checked before they are stored (spec FR-020; data-model.md, role_grants).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GrantValidationTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;
    private Guid _site;
    private Guid _user;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("grants").ConfigureAwait(false));
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
        _site = await UsersApi.CreateSiteAsync(_admin, "granting.test").ConfigureAwait(false);
        (_user, _) = await UsersApi.CreateUserAsync(_admin, "grantee@example.test").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_site_that_does_not_exist_is_refused_with_a_field_error()
    {
        var response = await UsersApi.ReplaceGrantsAsync(_admin, _user, UsersApi.Grants(false, (Guid.NewGuid(), "editor"))).ConfigureAwait(true);

        await AssertFieldErrorAsync(response, "Grants.SiteRoles[0].SiteId").ConfigureAwait(true);
    }

    [Fact]
    public async Task Two_roles_on_one_site_are_refused()
    {
        var response = await UsersApi.ReplaceGrantsAsync(_admin, _user, UsersApi.Grants(false, (_site, "editor"), (_site, "designer"))).ConfigureAwait(true);

        await AssertFieldErrorAsync(response, "Grants.SiteRoles").ConfigureAwait(true);
    }

    [Fact]
    public async Task Only_designer_and_editor_are_site_roles()
    {
        var response = await UsersApi.ReplaceGrantsAsync(_admin, _user, UsersApi.Grants(false, (_site, "administrator"))).ConfigureAwait(true);

        await AssertFieldErrorAsync(response, "Grants.SiteRoles[0].Role").ConfigureAwait(true);
    }

    /// <summary>
    /// FR-020: an administrator may also hold site roles; they are kept but add nothing.
    /// </summary>
    [Fact]
    public async Task Administrator_with_site_roles_is_accepted()
    {
        var response = await UsersApi.ReplaceGrantsAsync(_admin, _user, UsersApi.Grants(true, (_site, "editor"))).ConfigureAwait(true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("grants").GetProperty("administrator").GetBoolean());
        Assert.Single(body.GetProperty("grants").GetProperty("siteRoles").EnumerateArray());
    }

    [Fact]
    public async Task Creating_a_user_checks_grants_too()
    {
        var response = await UsersApi.CreateAsync(_admin, "badgrants@example.test", UsersApi.Grants(false, (Guid.NewGuid(), "designer"))).ConfigureAwait(true);

        await AssertFieldErrorAsync(response, "Grants.SiteRoles[0].SiteId").ConfigureAwait(true);
    }

    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, string field)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(
            body.GetProperty("errors").TryGetProperty(field, out _),
            $"Expected a field error on '{field}', got: {body.GetProperty("errors")}");
    }
}
