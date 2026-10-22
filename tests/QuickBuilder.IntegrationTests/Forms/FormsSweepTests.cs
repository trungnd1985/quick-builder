using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// The hourly sweep (018 FR-023a; research FS-17): a form's retention period removes its older
/// inbox submissions and records how many, and nothing else is touched.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FormsSweepTests(DatabaseFixture database)
{
    [Fact]
    public async Task Retention_removes_only_older_inbox_submissions_and_records_the_count()
    {
        using var world = await CreateAsync(database, "forms-sweep").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("short", "Short", ""","retentionDays":30"""), Form("kept", "Kept"))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);

        foreach (var (form, email) in new[] { ("short", "old@a.co"), ("short", "new@a.co"), ("kept", "old@b.co") })
        {
            await world.SubmitAsync(form, contact, "/contact", [new("f.e00000000001", email)], json: true).ConfigureAwait(true);
        }

        // Age the "old" submissions past 30 days, and add an aged spam copy the retention leaves alone.
        await world.WithFormsAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE forms.submissions SET received_at_utc = now() - interval '31 days' WHERE search_text LIKE '%old@%'")).ConfigureAwait(true);
        await world.WithFormsAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO forms.submissions (id, site_id, form_id, form_name, page_id, page_path, received_at_utc, values, search_text, fingerprint, is_read, is_spam, notification_state) " +
            "SELECT gen_random_uuid(), site_id, form_id, form_name, page_id, page_path, received_at_utc, values, search_text, fingerprint, false, true, 'none' FROM forms.submissions WHERE form_id = 'short' AND search_text LIKE '%old@%'")).ConfigureAwait(true);

        var removed = await world.App.Services.GetRequiredService<FormsSweepService>().RunOnceAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, removed);
        var left = await world.SubmissionsAsync().ConfigureAwait(true);
        Assert.DoesNotContain(left, s => s.FormId == "short" && !s.IsSpam && s.SearchText.Contains("old@", StringComparison.Ordinal));
        Assert.Contains(left, s => s.FormId == "short" && s.SearchText.Contains("new@", StringComparison.Ordinal));
        Assert.Contains(left, s => s.FormId == "kept" && s.SearchText.Contains("old@", StringComparison.Ordinal));
        Assert.Contains(left, s => s.FormId == "short" && s.IsSpam);

        var entry = await world.WithFormsAsync(db => db.ForSite<SubmissionLogEntry>(world.Site).AsNoTracking().SingleAsync()).ConfigureAwait(true);
        Assert.Equal(SubmissionLogActions.RetentionPurge, entry.Action);
        Assert.Equal("short", entry.FormId);
        Assert.Equal(1, entry.Count);
        Assert.Null(entry.ActorUserId);

        // A second sweep finds nothing more and records nothing.
        Assert.Equal(0, await world.App.Services.GetRequiredService<FormsSweepService>().RunOnceAsync(CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task More_than_one_batch_is_removed_in_one_sweep_and_recorded_once()
    {
        using var world = await CreateAsync(database, "forms-sweep-batches").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("short", "Short", ""","retentionDays":30"""))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        await world.SubmitAsync("short", contact, "/contact", [new("f.e00000000001", "a@a.co")], json: true).ConfigureAwait(true);
        var batches = FormsSweepService.BatchSize + 1;
        await world.WithFormsAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO forms.submissions (id, site_id, form_id, form_name, page_id, page_path, received_at_utc, values, search_text, fingerprint, is_read, is_spam, notification_state)
            SELECT gen_random_uuid(), s.site_id, s.form_id, s.form_name, s.page_id, s.page_path, now() - interval '40 days', s.values, s.search_text, s.fingerprint, true, false, 'not_needed'
            FROM forms.submissions s, generate_series(1, {batches}) WHERE s.form_id = 'short'
            """)).ConfigureAwait(true);

        var removed = await world.App.Services.GetRequiredService<FormsSweepService>().RunOnceAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(batches, removed);
        Assert.Single(await world.SubmissionsAsync().ConfigureAwait(true));
        var entry = await world.WithFormsAsync(db => db.ForSite<SubmissionLogEntry>(world.Site).AsNoTracking().SingleAsync()).ConfigureAwait(true);
        Assert.Equal(batches, entry.Count);
    }

    /// <summary>
    /// The module's filter bypasses are the per-site helper and the sweep's starting query, both
    /// in the context with their reasons (018 FR-024). US4 adds the notification worker's.
    /// </summary>
    [Fact]
    public void The_sweep_query_is_the_only_cross_site_read_besides_the_per_site_helper()
    {
        var module = Path.Combine(DraftApiTests.FindRepositoryRoot(), "src", "Modules", "Forms", "QuickBuilder.Forms");
        var bypasses = Directory.EnumerateFiles(module, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Where(l => l.Contains(".AcrossAllSites(", StringComparison.Ordinal)).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .ToList();

        Assert.Equal(2, bypasses.Count);
        Assert.All(bypasses, b => Assert.Equal("FormsDbContext.cs", b.File));
        Assert.Contains(bypasses, b => b.Line.Contains("retention sweep", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unpublished_form_keeps_its_last_published_retention()
    {
        using var world = await CreateAsync(database, "forms-sweep-gone").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("short", "Short", ""","retentionDays":30"""))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        await world.SubmitAsync("short", contact, "/contact", [new("f.e00000000001", "old@a.co")], json: true).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, contact)).ConfigureAwait(true)).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        await world.WithFormsAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE forms.submissions SET received_at_utc = now() - interval '31 days'")).ConfigureAwait(true);

        var removed = await world.App.Services.GetRequiredService<FormsSweepService>().RunOnceAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, removed);
        Assert.Empty(await world.SubmissionsAsync().ConfigureAwait(true));
    }
}
