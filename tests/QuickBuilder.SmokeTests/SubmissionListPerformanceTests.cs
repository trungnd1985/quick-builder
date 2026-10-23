using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 018 SC-006: with 100,000 submissions on a site, the list, its filters and a search answer in
/// under 2 s, and an export of 10,000 finishes in under 30 s. Also records the submit endpoint's
/// p95 against the plan's 300 ms goal.
/// </summary>
/// <remarks>
/// Measured against the built image on the reference compose setup (2 CPUs, 4 GB), with the rate
/// limit raised so one test address can submit repeatedly. The submissions are seeded in one SQL
/// statement in the shape the product stores them, as <see cref="ItemSeedSql"/> seeds items.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class SubmissionListPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "forms.perf.test";
    private const int Seeded = 100_000;
    private const int Repeats = 20;
    private const int Submits = 200;

    private static readonly TimeSpan ListBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExportBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SubmitGoal = TimeSpan.FromMilliseconds(300);

    private readonly ComposeDeployment _deployment = new("docker-compose.perf.yml", "docker-compose.forms-perf.yml");

    public async Task InitializeAsync()
    {
        if (!await _deployment.StartAsync().ConfigureAwait(false))
        {
            output.WriteLine("Docker is not available; performance test will skip.");
        }
    }

    public Task DisposeAsync() => _deployment.StopAsync();

    [SkippableFact]
    public async Task A_site_with_100000_submissions_lists_searches_and_exports_within_budget()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        var (siteId, pageId) = await FormPageAsync(client).ConfigureAwait(true);

        // The submit goal first, on an almost empty table, then the seeded volume.
        var submits = new List<TimeSpan>();
        for (var i = 0; i < Submits; i++)
        {
            var timer = Stopwatch.StartNew();
            await SubmitAsync(client, pageId, $"live{i}@example.com").ConfigureAwait(true);
            submits.Add(timer.Elapsed);
        }

        await SeedAsync(siteId).ConfigureAwait(true);

        var root = $"/api/v1/forms/sites/{siteId}/submissions";
        var list = await TimeAsync(client, root).ConfigureAwait(true);
        var filtered = await TimeAsync(client, $"{root}?formId=contact&read=unread").ConfigureAwait(true);
        var search = await TimeAsync(client, $"{root}?q=visitor77777").ConfigureAwait(true);
        var deep = await TimeAsync(client, $"{root}?q=example.com").ConfigureAwait(true);

        // An export of 10,000: the newest 10,000 seeded rows, one second apart.
        var to = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var from = DateTimeOffset.UtcNow.AddSeconds(-(10_000 + Submits + 60)).ToString("O", CultureInfo.InvariantCulture);
        var exportTimer = Stopwatch.StartNew();
        var csv = await client.GetStringAsync(new Uri($"{root}/export?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}", UriKind.Relative)).ConfigureAwait(true);
        var export = exportTimer.Elapsed;
        var rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length - 1;

        output.WriteLine(
            $"{Seeded} submissions: list p95 {Ms(list)}, filtered p95 {Ms(filtered)}, search p95 {Ms(search)}, broad search p95 {Ms(deep)}; " +
            $"export of {rows} rows {Ms(export)}; submit p50 {Ms(P(submits, 0.5))} p95 {Ms(P(submits, 0.95))} (goal {Ms(SubmitGoal)})");

        Assert.True(rows >= 10_000, $"The export held {rows} rows.");
        Assert.True(list <= ListBudget, $"SC-006: the list took {Ms(list)} at p95.");
        Assert.True(filtered <= ListBudget, $"SC-006: a filtered list took {Ms(filtered)} at p95.");
        Assert.True(search <= ListBudget, $"SC-006: a search took {Ms(search)} at p95.");
        Assert.True(deep <= ListBudget, $"SC-006: a broad search took {Ms(deep)} at p95.");
        Assert.True(export <= ExportBudget, $"SC-006: the export took {Ms(export)}.");
    }

    private async Task SeedAsync(Guid siteId)
    {
        var sql =
            "INSERT INTO forms.submissions (id, site_id, form_id, form_name, page_id, page_path, received_at_utc, \\\"values\\\", search_text, fingerprint, is_read, is_spam, notification_state) " +
            "SELECT gen_random_uuid(), s.site_id, s.form_id, s.form_name, s.page_id, s.page_path, now() - ((g.n + 300) || ' seconds')::interval, " +
            "jsonb_build_array(jsonb_build_object('fieldId', 'e00000000001', 'label', 'Email', 'type', 'email', 'value', 'visitor' || g.n || '@example.com'), " +
            "jsonb_build_object('fieldId', 'c00000000001', 'label', 'Message', 'type', 'textarea', 'value', 'Hello, this is enquiry number ' || g.n)), " +
            "'visitor' || g.n || '@example.com hello, this is enquiry number ' || g.n, decode(md5(g.n::text) || md5(g.n::text), 'hex'), g.n % 3 = 0, false, 'not_needed' " +
            $"FROM generate_series(1, {Seeded}) AS g(n) CROSS JOIN (SELECT site_id, form_id, form_name, page_id, page_path FROM forms.submissions WHERE site_id = '{siteId}' LIMIT 1) s; " +
            "ANALYZE forms.submissions;";

        var seed = await _deployment.ComposeAsync($"exec -T db psql -U quickbuilder -d quickbuilder -v ON_ERROR_STOP=1 -c \"{sql}\"").ConfigureAwait(false);
        Assert.True(seed.ExitCode == 0, $"Seeding {Seeded} submissions failed:\n{seed.Output}");
    }

    private static async Task<TimeSpan> TimeAsync(HttpClient client, string path)
    {
        var times = new List<TimeSpan>();
        for (var i = 0; i < Repeats; i++)
        {
            var timer = Stopwatch.StartNew();
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
            await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            times.Add(timer.Elapsed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return P(times, 0.95);
    }

    private static async Task<(Guid SiteId, Guid PageId)> FormPageAsync(HttpClient client)
    {
        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Forms perf", hostNames = new[] { Host } })
            .ConfigureAwait(false);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
        var pages = $"/api/v1/pages/sites/{siteId}/pages";

        using var created = await client.PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Contact", slug = "contact" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var pageId = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
        var subject = $"{pages}/{pageId}";
        const string Document = """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{},"children":[
              {"id":"contact","type":"form","props":{"name":"Contact","fields":[
                {"id":"e00000000001","type":"email","label":"Email","required":true},
                {"id":"c00000000001","type":"textarea","label":"Message"}]},"children":[]}]}]}}
            """;
        await PerfApi.PublishAsync(client, subject, await PerfApi.SaveAsync(client, subject, Document).ConfigureAwait(false)).ConfigureAwait(false);

        // The form becomes known through the outbox; wait until a submit is accepted.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (await SubmitAsync(client, pageId, "first@example.com", require: false).ConfigureAwait(false))
            {
                return (siteId, pageId);
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The published form never became live.");
    }

    private static async Task<bool> SubmitAsync(HttpClient client, Guid pageId, string email, bool require = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/_forms/contact", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([new("_page", pageId.ToString()), new("_path", "/contact"), new("f.e00000000001", email), new("f.c00000000001", "Timing a submission.")]),
        };
        request.Headers.Host = Host;
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        if (require)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task ClaimAsync(HttpClient client) =>
        await PerfApi.ClaimAsync(client, _deployment, "forms-perf@smoke.test").ConfigureAwait(false);

    private static TimeSpan P(IEnumerable<TimeSpan> samples, double percentile) => PerfApi.P(samples, percentile);

    private static string Ms(TimeSpan value) => PerfApi.Ms(value);
}
