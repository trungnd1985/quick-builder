using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// Notifications against a real SMTP server (018 FR-031 to FR-035, SC-005; research FS-17 to
/// FS-19): one email to every recipient, values and reply address only when the form says so,
/// retries on temporary refusals, at most one send, and nothing personal in the logs.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationTests(DatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private const string Notify = ""","notify":{"recipients":["editor@example.com","owner@example.com"],"replyFieldId":"e00000000001"}""";

    private readonly ManualClock _clock = new();

    public Task InitializeAsync() => mailpit.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(FormsWorld World, Guid Page)> WorldAsync(string label, string notify = Notify, bool email = true, bool realClock = false, bool publicUrl = true)
    {
        var settings = email
            ? mailpit.Settings().Where(s => s.Value is not null).ToDictionary(s => s.Key, s => s.Value!, StringComparer.Ordinal)
            : [];
        if (publicUrl)
        {
            settings["Hosting:PublicUrl"] = "https://cms.example.com";
        }

        var world = await CreateAsync(database, label, realClock ? null : s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock)), settings).ConfigureAwait(false);
        var page = await world.PageAsync("contact").ConfigureAwait(false);
        await world.PublishPageAsync(page, Document(Form("contact", "Contact", notify))).ConfigureAwait(false);
        await world.DeliverEventsAsync().ConfigureAwait(false);
        return (world, page);
    }

    private static Task<HttpResponseMessage> SubmitAsync(FormsWorld world, Guid page, string name = "Anna Nguyễn", string email = "anna@example.com") =>
        world.SubmitAsync("contact", page, "/contact", [new("f.e00000000001", email), new("f.f00000000001", name)], json: true);

    private static Task<int> RunWorkerAsync(FormsWorld world) =>
        world.App.Services.GetRequiredService<NotificationWorker>().RunOnceAsync(CancellationToken.None);

    private static Task<NotificationDelivery> DeliveryAsync(FormsWorld world) =>
        world.WithFormsAsync(db => db.ForSite<NotificationDelivery>(world.Site).AsNoTracking().SingleAsync());

    [Fact]
    public async Task One_email_reaches_every_recipient_with_the_values_the_link_and_the_reply_address()
    {
        var (world, page) = await WorldAsync("notify-send");
        using var _ = world;
        await SubmitAsync(world, page);
        var submission = Assert.Single(await world.SubmissionsAsync());
        Assert.Equal(NotificationStates.Pending, submission.NotificationState);

        Assert.Equal(1, await RunWorkerAsync(world));

        var message = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromMinutes(1)));
        Assert.Equal("New submission: Contact", message.Subject);
        Assert.Equal(["editor@example.com", "owner@example.com"], message.To.Order(StringComparer.Ordinal));
        Assert.Equal(["anna@example.com"], message.ReplyTo);
        Assert.Contains("Name: Anna Nguyễn", message.Text, StringComparison.Ordinal);
        Assert.Contains($"https://cms.example.com/admin/sites/{world.Site}/submissions/{submission.Id}", message.Text, StringComparison.Ordinal);
        Assert.Equal(NotificationStates.Sent, Assert.Single(await world.SubmissionsAsync()).NotificationState);
        Assert.Equal(DeliveryStates.Sent, (await DeliveryAsync(world)).State);
    }

    [Fact]
    public async Task Without_values_the_email_holds_none_and_has_no_reply_address()
    {
        var (world, page) = await WorldAsync("notify-novalues", ""","notify":{"recipients":["editor@example.com","owner@example.com"],"includeValues":false}""");
        using var _ = world;
        await SubmitAsync(world, page);

        await RunWorkerAsync(world);

        var message = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain("anna@example.com", message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Anna", message.Text, StringComparison.Ordinal);
        Assert.Empty(message.ReplyTo);
    }

    [Fact]
    public async Task A_value_cannot_add_a_header_or_a_recipient()
    {
        var (world, page) = await WorldAsync("notify-inject");
        using var _ = world;
        await SubmitAsync(world, page, name: "Anna\r\nBcc: spy@example.com\r\nSubject: hacked");

        await RunWorkerAsync(world);

        var message = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain("Bcc", message.Headers);
        Assert.Equal("New submission: Contact", message.Subject);
        Assert.Equal(2, message.To.Count);
    }

    [Fact]
    public async Task A_temporary_refusal_waits_and_the_next_attempt_sends()
    {
        var (world, page) = await WorldAsync("notify-retry");
        using var _ = world;
        await SubmitAsync(world, page);
        await mailpit.RefuseAsync(451);

        await RunWorkerAsync(world);

        var waiting = await DeliveryAsync(world);
        Assert.Equal(DeliveryStates.Pending, waiting.State);
        Assert.Equal(1, waiting.Attempts);
        Assert.Equal(_clock.GetUtcNow() + TimeSpan.FromMinutes(1), waiting.NextAttemptAtUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.StartsWith("451", waiting.LastError, StringComparison.Ordinal);
        Assert.Equal(NotificationStates.Pending, Assert.Single(await world.SubmissionsAsync()).NotificationState);

        // Not yet due: nothing is attempted.
        Assert.Equal(0, await RunWorkerAsync(world));

        await mailpit.RefuseAsync(null);
        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(1, await RunWorkerAsync(world));

        Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromSeconds(10)));
        var sent = await DeliveryAsync(world);
        Assert.Equal(DeliveryStates.Sent, sent.State);
        Assert.Equal(2, sent.Attempts);
    }

    [Fact]
    public async Task A_permanent_refusal_fails_at_once_with_the_reply()
    {
        var (world, page) = await WorldAsync("notify-permanent");
        using var _ = world;
        await SubmitAsync(world, page);
        await mailpit.RefuseAsync(550);

        await RunWorkerAsync(world);

        var failed = await DeliveryAsync(world);
        Assert.Equal(DeliveryStates.Failed, failed.State);
        Assert.StartsWith("550", failed.LastError, StringComparison.Ordinal);
        Assert.Equal(NotificationStates.Failed, Assert.Single(await world.SubmissionsAsync()).NotificationState);
    }

    [Fact]
    public async Task Six_temporary_refusals_fail_the_notification()
    {
        var (world, page) = await WorldAsync("notify-six");
        using var _ = world;
        await SubmitAsync(world, page);
        await mailpit.RefuseAsync(451);

        foreach (var wait in NotificationDelivery.RetryDelays.Append(TimeSpan.Zero))
        {
            Assert.Equal(1, await RunWorkerAsync(world));
            _clock.Advance(wait + TimeSpan.FromSeconds(1));
        }

        var failed = await DeliveryAsync(world);
        Assert.Equal(DeliveryStates.Failed, failed.State);
        Assert.Equal(NotificationDelivery.MaxAttempts, failed.Attempts);
        Assert.Equal(0, await RunWorkerAsync(world));
    }

    [Fact]
    public async Task A_claim_whose_lease_ran_out_fails_as_interrupted_and_is_never_sent()
    {
        var (world, page) = await WorldAsync("notify-lease");
        using var _ = world;
        await SubmitAsync(world, page);
        await world.WithFormsAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE forms.notification_deliveries SET state = 'sending', attempts = 1, next_attempt_at_utc = NULL, lease_until_utc = {_clock.GetUtcNow().AddMinutes(-1)}"));

        Assert.Equal(0, await RunWorkerAsync(world));

        var failed = await DeliveryAsync(world);
        Assert.Equal(DeliveryStates.Failed, failed.State);
        Assert.Equal(NotificationDelivery.InterruptedReason, failed.LastError);
        Assert.Equal(NotificationStates.Failed, Assert.Single(await world.SubmissionsAsync()).NotificationState);
        Assert.Empty(await mailpit.MessagesAsync());
    }

    [Fact]
    public async Task Two_workers_side_by_side_never_send_one_notification_twice()
    {
        var (world, page) = await WorldAsync("notify-twice");
        using var _ = world;
        for (var i = 0; i < 8; i++)
        {
            await SubmitAsync(world, page, name: $"Visitor {i}");
        }

        var runs = await Task.WhenAll(RunWorkerAsync(world), RunWorkerAsync(world), RunWorkerAsync(world));

        Assert.Equal(8, runs.Sum());
        var messages = await mailpit.WaitForAsync(8, TimeSpan.FromSeconds(20));
        Assert.Equal(8, messages.Count);
        Assert.Equal(8, messages.Select(m => m.Text).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Running_as_a_hosted_service_the_worker_sends_within_seconds_and_warns_without_a_public_url()
    {
        var (world, page) = await WorldAsync("notify-hosted", realClock: true, publicUrl: false);
        using var _ = world;
        await SubmitAsync(world, page);
        var worker = world.App.Services.GetRequiredService<NotificationWorker>();

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // SC-005: handed to the mail server within a minute; the poll is every 5 seconds.
            var message = Assert.Single(await mailpit.WaitForAsync(1, TimeSpan.FromMinutes(1)));
            Assert.Contains("Open Submissions in the admin to read it.", message.Text, StringComparison.Ordinal);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Contains(world.App.Logs.Events, e => LogCapture.Render(e).Contains("Hosting:PublicUrl is empty", StringComparison.Ordinal));
        await WaitForAsync(async () => (await DeliveryAsync(world)).State == DeliveryStates.Sent);
    }

    [Fact]
    public async Task A_waiting_notification_fails_when_its_form_no_longer_names_anyone()
    {
        var (world, page) = await WorldAsync("notify-nobody-left");
        using var _ = world;
        await SubmitAsync(world, page);
        await world.PublishPageAsync(page, Document(Form("contact", "Contact")));
        await world.DeliverEventsAsync();

        Assert.Equal(1, await RunWorkerAsync(world));

        var failed = await DeliveryAsync(world);
        Assert.Equal(DeliveryStates.Failed, failed.State);
        Assert.Equal("The form no longer has anyone to notify.", failed.LastError);
        Assert.Empty(await mailpit.MessagesAsync());
    }

    [Fact]
    public async Task A_deleted_submission_takes_its_waiting_notification_with_it()
    {
        var (world, page) = await WorldAsync("notify-deleted");
        using var _ = world;
        await SubmitAsync(world, page);
        var submission = Assert.Single(await world.SubmissionsAsync());

        using var deleted = await world.Admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/v1/forms/sites/{world.Site}/submissions", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { ids = new[] { submission.Id } }),
        });
        deleted.EnsureSuccessStatusCode();

        Assert.Equal(0, await RunWorkerAsync(world));
        Assert.Empty(await mailpit.MessagesAsync());
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 50 && !await condition(); i++)
        {
            await Task.Delay(100);
        }

        Assert.True(await condition());
    }

    [Fact]
    public async Task Without_email_a_notification_is_not_sent_and_none_is_queued()
    {
        var (world, page) = await WorldAsync("notify-off", email: false);
        using var _ = world;
        await SubmitAsync(world, page);

        Assert.Equal(NotificationStates.NotSent, Assert.Single(await world.SubmissionsAsync()).NotificationState);
        Assert.Equal(0, await world.WithFormsAsync(db => db.ForSite<NotificationDelivery>(world.Site).CountAsync()));
    }

    [Fact]
    public async Task Without_recipients_no_notification_is_needed()
    {
        var (world, page) = await WorldAsync("notify-nobody", notify: "");
        using var _ = world;
        await SubmitAsync(world, page);

        Assert.Equal(NotificationStates.NotNeeded, Assert.Single(await world.SubmissionsAsync()).NotificationState);
        Assert.Equal(0, await world.WithFormsAsync(db => db.ForSite<NotificationDelivery>(world.Site).CountAsync()));
    }

    [Fact]
    public async Task No_recipient_or_value_reaches_the_logs()
    {
        var (world, page) = await WorldAsync("notify-logs");
        using var _ = world;
        await SubmitAsync(world, page, name: "Zelda Quintessence");
        await mailpit.RefuseAsync(451);
        await RunWorkerAsync(world);
        await mailpit.RefuseAsync(null);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await RunWorkerAsync(world);

        var logs = world.App.Logs.Events.Select(LogCapture.Render).ToList();
        foreach (var secret in new[] { "editor@example.com", "owner@example.com", "anna@example.com", "Zelda Quintessence" })
        {
            Assert.DoesNotContain(logs, l => l.Contains(secret, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Raw SQL bypasses the site filter, so it stays in the context, each statement documented
    /// there with the site it states or why it has none: the worker's claim and lease expiry are
    /// the cross-site ones (018 FR-024).
    /// </summary>
    [Fact]
    public void Raw_sql_lives_only_in_the_context_where_the_workers_cross_site_claim_is_documented()
    {
        var module = Path.Combine(DraftApiTests.FindRepositoryRoot(), "src", "Modules", "Forms", "QuickBuilder.Forms");
        var raw = Directory.EnumerateFiles(module, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f)
                .Where(l => l.Contains("Database.SqlQuery", StringComparison.Ordinal) || l.Contains("Database.ExecuteSql", StringComparison.Ordinal) || l.Contains("FromSql", StringComparison.Ordinal))
                .Select(_ => Path.GetFileName(f)))
            .ToList();

        Assert.NotEmpty(raw);
        Assert.All(raw, file => Assert.Equal("FormsDbContext.cs", file));
        var context = File.ReadAllText(Path.Combine(module, "Infrastructure", "FormsDbContext.cs"));
        Assert.Contains("The notification worker's cross-site read", context, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE SKIP LOCKED", context, StringComparison.Ordinal);
    }
}
