using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Infrastructure;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>Typed calls to the user-management and account endpoints (contracts/identity-api.yaml).</summary>
internal static class UsersApi
{
    public static object Grants(bool administrator = false, params (Guid SiteId, string Role)[] siteRoles) =>
        new { administrator, siteRoles = siteRoles.Select(r => new { siteId = r.SiteId, role = r.Role }).ToArray() };

    public static Task<HttpResponseMessage> CreateAsync(HttpClient admin, string email, object? grants = null, string displayName = "New Person") =>
        admin.PostAsJsonAsync(new Uri("/api/v1/identity/users", UriKind.Relative), new { displayName, email, grants = grants ?? Grants() });

    /// <summary>Creates a user and returns its id and first set-password token.</summary>
    public static async Task<(Guid Id, string Token)> CreateUserAsync(HttpClient admin, string email, object? grants = null)
    {
        var response = await CreateAsync(admin, email, grants).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        return (body.GetProperty("user").GetProperty("id").GetGuid(), body.GetProperty("setPasswordLink").GetProperty("token").GetString()!);
    }

    public static Task<HttpResponseMessage> UpdateAsync(HttpClient admin, Guid userId, object patch) =>
        admin.PatchAsJsonAsync(new Uri($"/api/v1/identity/users/{userId}", UriKind.Relative), patch);

    public static Task<HttpResponseMessage> ReplaceGrantsAsync(HttpClient admin, Guid userId, object grants) =>
        admin.PutAsJsonAsync(new Uri($"/api/v1/identity/users/{userId}/grants", UriKind.Relative), grants);

    public static Task<HttpResponseMessage> DeactivateAsync(HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/v1/identity/users/{userId}/deactivate", UriKind.Relative), null);

    public static Task<HttpResponseMessage> ReactivateAsync(HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/v1/identity/users/{userId}/reactivate", UriKind.Relative), null);

    public static Task<HttpResponseMessage> IssueLinkAsync(HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/v1/identity/users/{userId}/set-password-links", UriKind.Relative), null);

    public static async Task<string> IssueLinkTokenAsync(HttpClient admin, Guid userId)
    {
        var response = await IssueLinkAsync(admin, userId).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("token").GetString()!;
    }

    public static Task<HttpResponseMessage> RevokeSessionsAsync(HttpClient admin, Guid userId) =>
        admin.PostAsync(new Uri($"/api/v1/identity/users/{userId}/sessions/revoke", UriKind.Relative), null);

    public static Task<HttpResponseMessage> CheckLinkAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync(new Uri("/api/v1/identity/set-password-links/check", UriKind.Relative), new { token });

    public static Task<HttpResponseMessage> SetPasswordAsync(HttpClient client, string token, string newPassword) =>
        client.PostAsJsonAsync(new Uri("/api/v1/identity/set-password", UriKind.Relative), new { token, newPassword });

    public static Task<HttpResponseMessage> ChangeOwnPasswordAsync(HttpClient client, string currentPassword, string newPassword) =>
        client.PostAsJsonAsync(new Uri("/api/v1/identity/me/password", UriKind.Relative), new { currentPassword, newPassword });

    public static Task<HttpResponseMessage> RevokeOtherSessionsAsync(HttpClient client) =>
        client.PostAsync(new Uri("/api/v1/identity/me/sessions/revoke-others", UriKind.Relative), null);

    public static Task<HttpResponseMessage> MeAsync(HttpClient client) =>
        client.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative));

    public static async Task<Guid> CreateSiteAsync(HttpClient admin, string hostName)
    {
        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = hostName, hostNames = new[] { hostName } }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    public static async Task<int> ActiveAdministratorCountAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await context.RoleGrants
            .Where(g => g.Role == QuickBuilder.Shared.Kernel.Authorization.SystemRole.Administrator)
            .Join(context.Users, g => g.UserId, u => u.Id, (g, u) => u)
            .CountAsync(u => u.Status == QuickBuilder.Identity.Domain.UserStatus.Active)
            .ConfigureAwait(false);
    }
}
