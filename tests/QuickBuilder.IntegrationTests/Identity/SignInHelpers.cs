using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>Shared steps for tests that sign in through the real endpoint.</summary>
internal static class SignInHelpers
{
    public static Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync(new Uri("/api/v1/identity/sign-in", UriKind.Relative), new { email, password });

    /// <summary>
    /// A client that appears to come from <paramref name="remoteAddress"/> and, optionally,
    /// presents cookies such as <c>qb_device</c> — as a returning browser would.
    /// </summary>
    public static HttpClient Browser(WebApplicationFactory<Program> app, string remoteAddress, params string[] cookies)
    {
        var client = SetupHelpers.ClientFrom(app, remoteAddress);
        if (cookies.Length > 0)
        {
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        }

        return client;
    }

    /// <summary>"name=value" for a cookie the response set, for sending back.</summary>
    public static string Cookie(HttpResponseMessage response, string name) =>
        $"{name}={SetupHelpers.CookieValue(response, name) ?? throw new InvalidOperationException($"No {name} cookie was set.")}";

    /// <summary>The body with its traceId removed, for comparing two refusals.</summary>
    public static async Task<string> BodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"\"");
    }
}
