using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using QuickBuilder.Shared.Infrastructure.Commands;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// <c>sites export | check | import</c> from the command line (020 FR-032 … FR-034,
/// contracts/operator-commands.md): the same outcomes as the admin, the documented exit codes,
/// and jobs recorded as the operator's.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SitesCommandTests(DatabaseFixture database) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("qb-sites-command-").FullName;

    [Fact]
    public async Task Export_writes_the_archive_never_overwrites_and_is_recorded_as_the_operator_s()
    {
        using var site = await FullSite.CreateAsync(database, "cli-export").ConfigureAwait(true);
        var file = Path.Combine(_directory, "acme.qbsite");

        var (exit, output) = await RunAsync(site.App, "sites", "export", "--site", site.Site.ToString(), "--out", file).ConfigureAwait(true);
        Assert.True(exit == 0, output);
        Assert.True(new FileInfo(file).Length > 0);
        await using (var archive = await TransferApi.OpenAsync(await File.ReadAllBytesAsync(file).ConfigureAwait(true)).ConfigureAwait(true))
        {
            Assert.Equal(site.Site, archive.Manifest.Source.SiteId);
        }

        // FR-031: the job is the operator's, with no member.
        var job = await JobsAsync(site.App).ConfigureAwait(true);
        Assert.Contains(job, j => j.Kind == "export" && j.Trigger == "cli" && j.StartedBy is null && j.State == "succeeded");

        // Never overwritten; by host name it is the same site.
        Assert.Equal(64, (await RunAsync(site.App, "sites", "export", "--site", "cms-a.test", "--out", file).ConfigureAwait(true)).Exit);
        var json = await RunAsync(site.App, "sites", "export", "--site", "cms-a.test", "--out", Path.Combine(_directory, "again.qbsite"), "--json").ConfigureAwait(true);
        Assert.Equal(0, json.Exit);
        // Logs share standard output; the result is the last line.
        using var printed = JsonDocument.Parse(json.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1]);
        Assert.Equal("succeeded", printed.RootElement.GetProperty("state").GetString());
        Assert.Equal("cli", printed.RootElement.GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task Export_all_writes_one_archive_per_site_and_an_unfinished_merge_check_does_not_hold_a_site()
    {
        using var site = await FullSite.CreateAsync(database, "cli-export-all").ConfigureAwait(true);

        // A merge check waiting for confirmation holds no slot (data-model §1).
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        var waiting = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        Assert.Equal("awaiting_confirmation", (await TransferApi.CheckAsync(site.App, site.Admin, waiting, "merge", site.Site).ConfigureAwait(true)).GetProperty("state").GetString());

        var (exit, output) = await RunAsync(site.App, "sites", "export", "--all", "--out", _directory).ConfigureAwait(true);
        Assert.True(exit == 0, output);
        var sites = await SiteCountAsync(site.App).ConfigureAwait(true);
        Assert.Equal(sites, Directory.GetFiles(_directory, "*.qbsite").Length);
        Assert.Contains(Directory.GetFiles(_directory), f => Path.GetFileName(f).StartsWith("cms-a.test-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_as_new_needs_warnings_accepted_then_creates_the_site()
    {
        using var site = await FullSite.CreateAsync(database, "cli-import-new").ConfigureAwait(true);
        var file = Path.Combine(_directory, "acme.qbsite");
        Assert.Equal(0, (await RunAsync(site.App, "sites", "export", "--site", site.Site.ToString(), "--out", file).ConfigureAwait(true)).Exit);

        // The seeded file with no bytes is a warning (FR-015).
        Assert.Equal(6, (await RunAsync(site.App, "sites", "import", file, "--as-new", "--name", "From the command line", "--host-name", "cli-copy.test").ConfigureAwait(true)).Exit);
        // The archive's host names are this site's, so a new one is needed (FR-020).
        Assert.Equal(64, (await RunAsync(site.App, "sites", "import", file, "--as-new", "--name", "From the command line", "--accept-warnings").ConfigureAwait(true)).Exit);

        var before = await SiteCountAsync(site.App).ConfigureAwait(true);
        var (exit, output) = await RunAsync(site.App, "sites", "import", file, "--as-new", "--name", "From the command line", "--host-name", "cli-copy.test", "--accept-warnings").ConfigureAwait(true);
        Assert.True(exit == 0, output);
        Assert.Equal(before + 1, await SiteCountAsync(site.App).ConfigureAwait(true));
        var sites = await PagesApi.OkAsync(await site.Admin.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("From the command line", sites.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_into_a_site_with_a_policy_and_a_restore_with_its_typed_name()
    {
        using var site = await FullSite.CreateAsync(database, "cli-import-existing").ConfigureAwait(true);
        var file = Path.Combine(_directory, "acme.qbsite");
        Assert.Equal(0, (await RunAsync(site.App, "sites", "export", "--site", site.Site.ToString(), "--out", file).ConfigureAwait(true)).Exit);

        var merged = await RunAsync(site.App, "sites", "import", file, "--into", "cms-a.test", "--on-conflict", "skip", "--accept-warnings").ConfigureAwait(true);
        Assert.True(merged.Exit == 0, merged.Output);

        // A page added after the backup is removed by the restore, which first keeps the site as it was.
        await PagesApi.CreateOkAsync(site.Admin, site.Site, "news").ConfigureAwait(true);
        Assert.Equal(6, (await RunAsync(site.App, "sites", "import", file, "--restore", site.Site.ToString(), "--accept-warnings").ConfigureAwait(true)).Exit);
        Assert.Equal(6, (await RunAsync(site.App, "sites", "import", file, "--restore", site.Site.ToString(), "--confirm", "Not it", "--accept-warnings").ConfigureAwait(true)).Exit);
        Assert.Contains("/news", await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true));

        var before = Path.Combine(_directory, "before.qbsite");
        var (exit, output) = await RunAsync(
            site.App, "sites", "import", file, "--restore", site.Site.ToString(), "--confirm", "Site A", "--pre-restore-out", before, "--accept-warnings").ConfigureAwait(true);
        Assert.True(exit == 0, output);
        Assert.DoesNotContain("/news", await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true));
        Assert.True(new FileInfo(before).Length > 0);
    }

    [Fact]
    public async Task Every_other_exit_code_is_produced_by_its_case()
    {
        using var site = await FullSite.CreateAsync(database, "cli-exit-codes").ConfigureAwait(true);
        var file = Path.Combine(_directory, "acme.qbsite");
        Assert.Equal(0, (await RunAsync(site.App, "sites", "export", "--site", site.Site.ToString(), "--out", file).ConfigureAwait(true)).Exit);

        // 0: a check changes nothing and prints its result.
        var check = await RunAsync(site.App, "sites", "check", file, "--into", site.Site.ToString()).ConfigureAwait(true);
        Assert.True(check.Exit == 0, check.Output);
        Assert.Contains("conflicts:", check.Output, StringComparison.Ordinal);

        // 2: refused at check.
        var broken = Path.Combine(_directory, "broken.qbsite");
        await File.WriteAllBytesAsync(broken, "not an archive"u8.ToArray()).ConfigureAwait(true);
        Assert.Equal(2, (await RunAsync(site.App, "sites", "check", broken).ConfigureAwait(true)).Exit);

        // 4: unknown site.
        Assert.Equal(4, (await RunAsync(site.App, "sites", "export", "--site", "nowhere.test", "--out", Path.Combine(_directory, "x.qbsite")).ConfigureAwait(true)).Exit);
        Assert.Equal(4, (await RunAsync(site.App, "sites", "check", file, "--into", Guid.NewGuid().ToString()).ConfigureAwait(true)).Exit);

        // 5: the site already has an export queued.
        await TransferApi.StartExportOkAsync(site.Admin, site.Site).ConfigureAwait(true);
        Assert.Equal(5, (await RunAsync(site.App, "sites", "export", "--site", site.Site.ToString(), "--out", Path.Combine(_directory, "busy.qbsite")).ConfigureAwait(true)).Exit);

        // 64: usage.
        Assert.Equal(64, (await RunAsync(site.App, "sites").ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(site.App, "sites", "export", "--site", site.Site.ToString()).ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(site.App, "sites", "export", "--all", "--site", site.Site.ToString(), "--out", _directory).ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(site.App, "sites", "import", file, "--into", site.Site.ToString(), "--on-conflict", "merge").ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(site.App, "sites", "import", file, "--as-new").ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(site.App, "sites", "frobnicate").ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(site.App, "sites", "check", file, "--unknown").ConfigureAwait(true)).Exit);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temporary directory is the system's to clean.
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(QuickBuilderApp app, params string[] args)
    {
        var original = Console.Out;
        await using var captured = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Console.SetOut(captured);
        try
        {
            var exit = await HostCommandRunner.TryRunAsync(args, app.Services).ConfigureAwait(false);
            return (exit ?? -1, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static async Task<List<(string Kind, string Trigger, Guid? StartedBy, string State)>> JobsAsync(QuickBuilderApp app)
    {
        await using var connection = new Npgsql.NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand("SELECT kind, trigger, started_by, state FROM sites.transfer_jobs", connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var jobs = new List<(string, string, Guid?, string)>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            jobs.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3)));
        }

        return jobs;
    }

    private static async Task<int> SiteCountAsync(QuickBuilderApp app)
    {
        await using var connection = new Npgsql.NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM sites.sites", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }
}
