using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>
/// Captures, from a running deployment, what a later release must keep: every published
/// page as visitors see it and the row count of every table (019 contracts/snapshot.md §2–§3).
/// </summary>
/// <remarks>
/// "Every published page" is what each site's sitemap lists — pages and item pages, as a
/// search engine finds them — plus, per site, one address that must stay "not found" and
/// every exact redirect. A disabled site contributes its home address, which must keep
/// answering as a disabled site does.
/// </remarks>
internal static partial class ReleaseExpectations
{
    /// <summary>An address no page may ever have: it must answer the site's not-found page.</summary>
    public const string MissingPath = "/qb-release-gate-missing";

    public static async Task<Expectations> CaptureAsync(
        ComposeDeployment deployment,
        HttpClient admin,
        string createdBy,
        string createdFrom,
        IReadOnlyList<ExpectedMember> members,
        IReadOnlyList<ExpectedDraft> drafts,
        bool checkVolatileList = true)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(admin);

        return new Expectations
        {
            CreatedBy = createdBy,
            CreatedFrom = createdFrom,
            Pages = await CapturePagesAsync(admin).ConfigureAwait(false),
            Records = await CountRowsAsync(deployment, checkVolatileList).ConfigureAwait(false),
            Members = members,
            Drafts = drafts,
        };
    }

    /// <summary>Every site's pages, as a visitor sees them now.</summary>
    public static async Task<IReadOnlyList<ExpectedPage>> CapturePagesAsync(HttpClient admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        using var visitor = Visitor();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pages = new List<ExpectedPage>();

        var sites = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(false);
        foreach (var site in sites.EnumerateArray())
        {
            var host = site.GetProperty("primaryHostName").GetString()!;
            if (string.Equals(site.GetProperty("status").GetString(), "disabled", StringComparison.Ordinal))
            {
                pages.Add(await FetchAsync(visitor, host, "/").ConfigureAwait(false));
                continue;
            }

            var paths = new SortedSet<string>(StringComparer.Ordinal) { MissingPath };
            using (var sitemap = await GetAsync(visitor, host, "/sitemap.xml").ConfigureAwait(false))
            {
                if (sitemap.StatusCode == HttpStatusCode.OK)
                {
                    var xml = await sitemap.Content.ReadAsStringAsync().ConfigureAwait(false);
                    foreach (Match loc in Location().Matches(xml))
                    {
                        paths.Add(new Uri(WebUtility.HtmlDecode(loc.Groups["url"].Value)).PathAndQuery);
                    }
                }
            }

            var id = site.GetProperty("id").GetGuid();
            var redirects = await admin.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/pages/sites/{id}/redirects?limit=200", UriKind.Relative)).ConfigureAwait(false);
            foreach (var redirect in redirects.GetProperty("items").EnumerateArray())
            {
                if (redirect.GetProperty("match").GetString() == "exact")
                {
                    paths.Add(redirect.GetProperty("source").GetString()!);
                }
            }

            foreach (var path in paths)
            {
                var page = await FetchAsync(visitor, host, path).ConfigureAwait(false);
                Assert.False(
                    Expectations.ShowsDate(page.Text, today),
                    $"{page.Address} shows today's date, so it would differ when the snapshot is restored on another day (snapshot.md §3, time-dependent page).");
                pages.Add(page);
            }
        }

        return pages;
    }

    /// <summary>
    /// The row count of every table outside the volatile list, by <c>schema.table</c>. Fails when
    /// a volatile entry matches no table, which means the list has gone stale; checked only on the
    /// database of the release under test, since an older one may predate a listed table.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, long>> CountRowsAsync(ComposeDeployment deployment, bool checkVolatileList = true)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        const string Query =
            "SELECT table_schema || '.' || table_name || '|' || " +
            "(xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text " +
            "FROM information_schema.tables WHERE table_type = 'BASE TABLE' " +
            "AND table_schema NOT IN ('pg_catalog', 'information_schema', 'public') ORDER BY 1";

        var (exitCode, output) = await deployment
            .ComposeAsync($"exec -T db psql -U quickbuilder -d quickbuilder -At -c \"{Query}\"")
            .ConfigureAwait(false);
        Assert.True(exitCode == 0, $"Counting rows failed:\n{output}");

        var counts = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('|', StringComparison.Ordinal))
            .Select(line => line.Split('|'))
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.Ordinal);

        // Only on the release under test: an older database may predate a listed table.
        var stale = checkVolatileList ? Expectations.StaleVolatileEntries(counts.Keys) : [];
        Assert.True(stale.Count == 0, "These volatile-table entries match no table; update Expectations.VolatileTables:\n  " + string.Join("\n  ", stale));

        return counts
            .Where(c => !Expectations.IsVolatile(c.Key))
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// A hash of every table's rows with their <c>xmin</c>, so any insert, update or delete
    /// shows — even one that writes the same values (SC-006, SC-007). Volatile tables are left
    /// out: their rows move for reasons unrelated to content.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> FingerprintTablesAsync(ComposeDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        const string Query =
            "SELECT table_schema || '.' || table_name || '|' || " +
            "(xpath('/row/h/text()', query_to_xml(format('SELECT md5(coalesce(string_agg(x.xmin::text || x::text, ''|'' ORDER BY x.xmin::text || x::text), '''')) AS h FROM %I.%I x', table_schema, table_name), false, true, '')))[1]::text " +
            "FROM information_schema.tables WHERE table_type = 'BASE TABLE' " +
            "AND table_schema NOT IN ('pg_catalog', 'information_schema', 'public') ORDER BY 1";

        var (exitCode, output) = await deployment
            .ComposeAsync($"exec -T db psql -U quickbuilder -d quickbuilder -At -c \"{Query}\"")
            .ConfigureAwait(false);
        Assert.True(exitCode == 0, $"Fingerprinting tables failed:\n{output}");

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('|', StringComparison.Ordinal))
            .Select(line => line.Split('|', 2))
            .Where(parts => !Expectations.IsVolatile(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    /// <summary>A visitor's client: no session, and redirects reported rather than followed.</summary>
    public static HttpClient Visitor() =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };

    private static async Task<ExpectedPage> FetchAsync(HttpClient visitor, string host, string path)
    {
        using var response = await GetAsync(visitor, host, path).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // A redirect's visible form is where it leads.
        if (response.Headers.Location is { } location)
        {
            body = $"<body><a href=\"{WebUtility.HtmlEncode(location.OriginalString)}\">redirect</a></body>";
        }

        var text = VisibleForm.Text(body);
        return new ExpectedPage(
            host,
            path,
            status,
            VisibleForm.Fingerprint(body),
            text.Length <= ExpectedPage.TextLength ? text : text[..ExpectedPage.TextLength]);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient visitor, string host, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return await visitor.SendAsync(request).ConfigureAwait(false);
    }

    [GeneratedRegex(@"<loc>(?<url>[^<]+)</loc>", RegexOptions.CultureInvariant)]
    private static partial Regex Location();
}
