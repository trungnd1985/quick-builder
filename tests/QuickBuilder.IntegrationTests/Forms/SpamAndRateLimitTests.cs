using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Forms.Domain;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// Spam and abuse (018 US5; FR-016 to FR-021, SC-004; research FS-12 to FS-14): the trap field,
/// the per-address rate limit shared by every instance, duplicate suppression, the Spam list
/// with restore, the counters, and no client address kept outside the rate windows.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SpamAndRateLimitTests(DatabaseFixture database)
{
    private const string Email = "f.e00000000001";
    private const string Notify = ""","notify":{"recipients":["editor@example.com"]}""";

    // A mail server nobody listens on: email counts as configured, and nothing is ever sent
    // because no test runs the worker.
    private static readonly Dictionary<string, string> EmailOn = new(StringComparer.Ordinal)
    {
        ["Email:Smtp:Host"] = "127.0.0.1",
        ["Email:Smtp:Port"] = "1",
        ["Email:Smtp:Security"] = "None",
        ["Email:Smtp:From"] = "site@example.com",
    };

    private static async Task<(FormsWorld World, Guid Page)> WorldAsync(
        DatabaseFixture database, string label, Dictionary<string, string>? settings = null, ManualClock? clock = null)
    {
        var world = await CreateAsync(
            database,
            label,
            clock is null ? null : s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)),
            new Dictionary<string, string>(EmailOn.Concat(settings ?? []), StringComparer.Ordinal)).ConfigureAwait(false);
        var page = await world.PageAsync("contact").ConfigureAwait(false);
        await world.PublishPageAsync(page, Document(Form("contact", "Contact", Notify))).ConfigureAwait(false);
        await world.DeliverEventsAsync().ConfigureAwait(false);
        return (world, page);
    }

    private static Dictionary<string, string> Limits(int perMinute, int perHour) => new(StringComparer.Ordinal)
    {
        ["Forms:RateLimit:PerMinute"] = perMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Forms:RateLimit:PerHour"] = perHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static Task<int> DeliveriesAsync(FormsWorld world) =>
        world.WithFormsAsync(db => db.ForSite<NotificationDelivery>(world.Site).CountAsync());

    private static Task<SpamCounter?> CounterAsync(FormsWorld world) =>
        world.WithFormsAsync(db => db.ForSite<SpamCounter>(world.Site).AsNoTracking().SingleOrDefaultAsync());

    [Fact]
    public async Task A_filled_trap_with_valid_values_is_kept_as_spam_and_answered_as_a_success()
    {
        var (world, page) = await WorldAsync(database, "spam-kept");
        using var _ = world;

        var ok = await world.SubmitAsync("contact", page, "/contact", [new(Email, "real@example.com")], json: true);
        var trappedJson = await world.SubmitAsync("contact", page, "/contact", [new(Email, "bot@example.com"), new("_hp", "x")], json: true);
        var okHtml = await world.SubmitAsync("contact", page, "/contact", [new(Email, "real2@example.com")]);
        var trappedHtml = await world.SubmitAsync("contact", page, "/contact", [new(Email, "bot2@example.com"), new("_hp", "x")]);

        Assert.Equal(await ok.Content.ReadAsStringAsync(), await trappedJson.Content.ReadAsStringAsync());
        Assert.Equal(ok.StatusCode, trappedJson.StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, trappedHtml.StatusCode);
        Assert.Equal(okHtml.Headers.Location, trappedHtml.Headers.Location);

        var spam = (await world.SubmissionsAsync()).Where(s => s.IsSpam).ToList();
        Assert.Equal(2, spam.Count);
        Assert.All(spam, s => Assert.Equal(NotificationStates.None, s.NotificationState));
        Assert.Equal(2, await DeliveriesAsync(world));
        Assert.Equal(2, (await CounterAsync(world))!.TrappedKept);
    }

    [Fact]
    public async Task A_filled_trap_with_invalid_values_stores_nothing_and_is_answered_as_a_success()
    {
        var (world, page) = await WorldAsync(database, "spam-dropped");
        using var _ = world;

        var ok = await world.SubmitAsync("contact", page, "/contact", [new(Email, "real@example.com")], json: true);
        var dropped = await world.SubmitAsync("contact", page, "/contact", [new(Email, "not an address"), new("_hp", "x")], json: true);

        Assert.Equal(await ok.Content.ReadAsStringAsync(), await dropped.Content.ReadAsStringAsync());
        Assert.Single(await world.SubmissionsAsync());
        Assert.Equal(1, (await CounterAsync(world))!.TrappedDropped);
    }

    [Fact]
    public async Task Over_the_minute_limit_a_visitor_is_told_to_wait_and_others_are_not_affected()
    {
        var (world, page) = await WorldAsync(database, "rate-minute", Limits(5, 30));
        using var _ = world;
        for (var i = 0; i < 5; i++)
        {
            (await world.SubmitAsync("contact", page, "/contact", [new(Email, $"v{i}@example.com")], json: true, from: "203.0.113.7")).EnsureSuccessStatusCode();
        }

        var json = await world.SubmitAsync("contact", page, "/contact", [new(Email, "v5@example.com")], json: true, from: "203.0.113.7");
        var html = await world.SubmitAsync("contact", page, "/contact", [new(Email, "kept-value@example.com")], from: "203.0.113.7");
        var other = await world.SubmitAsync("contact", page, "/contact", [new(Email, "other@example.com")], json: true, from: "203.0.113.8");

        Assert.Equal(HttpStatusCode.TooManyRequests, json.StatusCode);
        using (var body = JsonDocument.Parse(await json.Content.ReadAsStringAsync()))
        {
            Assert.Equal("limited", body.RootElement.GetProperty("status").GetString());
            Assert.InRange(body.RootElement.GetProperty("retryAfter").GetInt32(), 1, 60);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, html.StatusCode);
        Assert.InRange(html.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        var page429 = await html.Content.ReadAsStringAsync();
        Assert.Contains("kept-value@example.com", page429, StringComparison.Ordinal);
        Assert.Contains("qb-form__banner", page429, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(6, (await world.SubmissionsAsync()).Count);
        Assert.Equal(2, (await CounterAsync(world))!.RateLimited);
    }

    [Fact]
    public async Task Over_the_hour_limit_a_visitor_waits_for_the_hour_to_end()
    {
        var clock = new ManualClock();
        var (world, page) = await WorldAsync(database, "rate-hour", Limits(3, 4), clock);
        using var _ = world;
        clock.Advance(TimeSpan.FromSeconds(60 - clock.GetUtcNow().Second));

        for (var i = 0; i < 3; i++)
        {
            (await world.SubmitAsync("contact", page, "/contact", [new(Email, $"a{i}@example.com")], json: true)).EnsureSuccessStatusCode();
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        (await world.SubmitAsync("contact", page, "/contact", [new(Email, "b@example.com")], json: true)).EnsureSuccessStatusCode();
        var limited = await world.SubmitAsync("contact", page, "/contact", [new(Email, "c@example.com")], json: true);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var now = clock.GetUtcNow();
        var endOfHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        Assert.Equal(Math.Ceiling((endOfHour - now).TotalSeconds), limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 0);
    }

    [Fact]
    public async Task Another_site_counts_the_same_address_separately()
    {
        var (world, page) = await WorldAsync(database, "rate-sites", Limits(1, 1));
        using var _ = world;
        var pageB = await world.PageAsync("contact", world.SiteB);
        await world.PublishPageAsync(pageB, Document(Form("contact", "Contact B")), world.SiteB);
        await world.DeliverEventsAsync();

        (await world.SubmitAsync("contact", page, "/contact", [new(Email, "a@example.com")], json: true)).EnsureSuccessStatusCode();
        var limitedOnA = await world.SubmitAsync("contact", page, "/contact", [new(Email, "b@example.com")], json: true);
        var acceptedOnB = await world.SubmitAsync("contact", pageB, "/contact", [new(Email, "c@example.com")], json: true, host: CmsScenario.HostB);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedOnA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, acceptedOnB.StatusCode);
    }

    [Fact]
    public async Task Two_instances_on_one_database_share_the_limit()
    {
        var (world, page) = await WorldAsync(database, "rate-instances", Limits(4, 30));
        using var _ = world;
        using var second = new QuickBuilderApp(world.App.ConnectionString, settings: new Dictionary<string, string>(EmailOn.Concat(Limits(4, 30)), StringComparer.Ordinal));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var app = i % 2 == 0 ? world.App : second;
            using var response = await SubmitAsync(app, "contact", page, "/contact", [new(Email, $"i{i}@example.com")], json: true);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Behind_a_named_proxy_the_forwarded_client_is_counted()
    {
        var settings = new Dictionary<string, string>(Limits(1, 1), StringComparer.Ordinal) { ["Hosting:KnownProxies:0"] = "10.0.0.1" };
        var (world, page) = await WorldAsync(database, "rate-proxy", settings);
        using var _ = world;

        void Via(HttpRequestMessage request, string client)
        {
            request.Headers.Add(IdentitySeed.RemoteAddressHeader, "10.0.0.1");
            request.Headers.Add("X-Forwarded-For", client);
        }

        var first = await SubmitAsync(world.App, "contact", page, "/contact", [new(Email, "a@example.com")], json: true, adjust: r => Via(r, "198.51.100.1"));
        var otherClient = await SubmitAsync(world.App, "contact", page, "/contact", [new(Email, "b@example.com")], json: true, adjust: r => Via(r, "198.51.100.2"));
        var sameClient = await SubmitAsync(world.App, "contact", page, "/contact", [new(Email, "c@example.com")], json: true, adjust: r => Via(r, "198.51.100.1"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherClient.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, sameClient.StatusCode);
    }

    [Fact]
    public async Task Addresses_in_one_IPv6_64_share_a_limit()
    {
        var (world, page) = await WorldAsync(database, "rate-ipv6", Limits(1, 1));
        using var _ = world;

        var first = await world.SubmitAsync("contact", page, "/contact", [new(Email, "a@example.com")], json: true, from: "2001:db8:1:2::10");
        var sameNetwork = await world.SubmitAsync("contact", page, "/contact", [new(Email, "b@example.com")], json: true, from: "2001:db8:1:2::99");
        var otherNetwork = await world.SubmitAsync("contact", page, "/contact", [new(Email, "c@example.com")], json: true, from: "2001:db8:1:3::10");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, sameNetwork.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherNetwork.StatusCode);
    }

    [Fact]
    public async Task The_same_submission_twice_within_ten_seconds_is_stored_once()
    {
        var clock = new ManualClock();
        var (world, page) = await WorldAsync(database, "spam-duplicate", clock: clock);
        using var _ = world;

        var first = await world.SubmitAsync("contact", page, "/contact", [new(Email, "same@example.com")], json: true);
        var again = await world.SubmitAsync("contact", page, "/contact", [new(Email, "same@example.com")], json: true);

        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Single(await world.SubmissionsAsync());
        Assert.Equal(1, await DeliveriesAsync(world));

        clock.Advance(TimeSpan.FromSeconds(11));
        (await world.SubmitAsync("contact", page, "/contact", [new(Email, "same@example.com")], json: true)).EnsureSuccessStatusCode();
        Assert.Equal(2, (await world.SubmissionsAsync()).Count);
    }

    [Fact]
    public async Task Spam_stays_out_of_the_inbox_and_can_be_restored_with_its_notification()
    {
        var (world, page) = await WorldAsync(database, "spam-restore");
        using var _ = world;
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "visitor@example.com"), new("_hp", "autofill")], json: true);
        var spam = Assert.Single(await world.SubmissionsAsync());
        var submissions = $"/api/v1/forms/sites/{world.Site}/submissions";

        Assert.DoesNotContain(spam.Id.ToString(), await world.Admin.GetStringAsync(new Uri(submissions, UriKind.Relative)), StringComparison.Ordinal);
        Assert.DoesNotContain(spam.Id.ToString(), await world.Admin.GetStringAsync(new Uri($"{submissions}?q=visitor", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Equal("""{"count":0}""", await world.Admin.GetStringAsync(new Uri($"{submissions}/unread-count", UriKind.Relative)));
        Assert.DoesNotContain("visitor@example.com", await world.Admin.GetStringAsync(new Uri($"{submissions}/export", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Contains(spam.Id.ToString(), await world.Admin.GetStringAsync(new Uri($"{submissions}?box=spam", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Equal(0, await DeliveriesAsync(world));

        var restored = await world.Admin.PostAsync(new Uri($"{submissions}/{spam.Id}/restore", UriKind.Relative), null);
        var again = await world.Admin.PostAsync(new Uri($"{submissions}/{spam.Id}/restore", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var now = Assert.Single(await world.SubmissionsAsync());
        Assert.False(now.IsSpam);
        Assert.False(now.IsRead);
        Assert.Equal(NotificationStates.Pending, now.NotificationState);
        Assert.Equal(1, await DeliveriesAsync(world));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("forms.not_spam", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await world.WithFormsAsync(db => db.ForSite<SubmissionLogEntry>(world.Site).CountAsync(e => e.Action == SubmissionLogActions.Restore)));
    }

    [Fact]
    public async Task The_counters_sum_each_form_over_the_last_thirty_days()
    {
        var (world, page) = await WorldAsync(database, "spam-counters", Limits(3, 30));
        using var _ = world;
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "a@example.com"), new("_hp", "x")], json: true);
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "nope"), new("_hp", "x")], json: true);
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "b@example.com")], json: true);
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "c@example.com")], json: true);
        await world.WithFormsAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO forms.spam_counters (site_id, form_id, day, trapped_kept, trapped_dropped, rate_limited) " +
            "SELECT site_id, form_id, day - 40, 100, 100, 100 FROM forms.spam_counters"));

        var body = await world.Admin.GetStringAsync(new Uri($"/api/v1/forms/sites/{world.Site}/spam-counters", UriKind.Relative));

        Assert.Equal("""{"items":[{"formId":"contact","trappedKept":1,"trappedDropped":1,"rateLimited":1}]}""", body);
    }

    [Fact]
    public async Task No_client_address_is_kept_outside_the_rate_windows()
    {
        const string Address = "203.0.113.99";
        var (world, page) = await WorldAsync(database, "spam-privacy", Limits(1, 1));
        using var _ = world;
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "a@example.com")], json: true, from: Address);
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "b@example.com")], json: true, from: Address);
        await world.SubmitAsync("contact", page, "/contact", [new(Email, "c@example.com"), new("_hp", "x")], json: true, from: "203.0.113.100");

        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Address)));
        var found = await world.WithFormsAsync(db => db.Database.SqlQuery<string>($"""
            SELECT t.table_name AS "Value" FROM information_schema.tables t
            WHERE t.table_schema = 'forms' AND t.table_type = 'BASE TABLE'
            """).ToListAsync());

        foreach (var table in found)
        {
            // The table names come from the schema's own catalogue, not from input.
#pragma warning disable EF1002
            var rows = await world.WithFormsAsync(db => db.Database.SqlQueryRaw<string>(
                $"SELECT row_to_json(r)::text AS \"Value\" FROM forms.\"{table}\" r").ToListAsync());
#pragma warning restore EF1002
            var text = string.Join('\n', rows);
            Assert.False(text.Contains(Address, StringComparison.Ordinal), $"forms.{table} holds the client address.");
            if (table != "rate_windows")
            {
                Assert.False(text.Contains(hash, StringComparison.Ordinal), $"forms.{table} holds the client address's hash.");
            }
        }

        var windows = await world.WithFormsAsync(db => db.ForSite<RateWindow>(world.Site).AsNoTracking().ToListAsync());
        Assert.Contains(windows, w => Convert.ToHexStringLower(w.ClientKey) == hash);
    }
}
