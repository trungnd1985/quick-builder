using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace QuickBuilder.SmokeTests;

/// <summary>The steps the forms performance tests share: claiming, saving, publishing and percentiles.</summary>
internal static class PerfApi
{
    /// <summary>Claims the fresh installation and signs the client in as its administrator.</summary>
    public static async Task ClaimAsync(HttpClient client, ComposeDeployment deployment, string email)
    {
        var setupCode = await deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Forms Perf Operator", email, password = "forms performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    /// <summary>Saves over the current draft and returns the new draft version.</summary>
    public static async Task<uint> SaveAsync(HttpClient client, string subject, string document)
    {
        using var current = await client.GetAsync(new Uri($"{subject}/draft", UriKind.Relative)).ConfigureAwait(false);
        var version = (await current.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("version").GetUInt32();

        using var body = new StringContent($$"""{"version":{{version}},"document":{{document}}}""", System.Text.Encoding.UTF8, "application/json");
        using var saved = await client.PutAsync(new Uri($"{subject}/draft", UriKind.Relative), body).ConfigureAwait(false);
        var reply = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"Saving a draft failed: {reply}");
        return JsonDocument.Parse(reply).RootElement.GetProperty("version").GetUInt32();
    }

    public static async Task PublishAsync(HttpClient client, string subject, uint draftVersion)
    {
        using var published = await client
            .PostAsJsonAsync(new Uri($"{subject}/publish", UriKind.Relative), new { draftVersion })
            .ConfigureAwait(false);
        Assert.True(published.StatusCode == HttpStatusCode.OK, $"Publishing failed: {await published.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    }

    public static TimeSpan P(IEnumerable<TimeSpan> samples, double percentile)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * percentile) - 1)];
    }

    public static string Ms(TimeSpan value) => $"{value.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms";
}
