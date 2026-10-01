using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Storage;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// SC-014: after media is copied to another provider with one site's files left out, the
/// consistency check reports exactly those files (013 research MV-21,
/// contracts/storage-providers.md §6).
/// </summary>
/// <remarks>
/// The library is built on Local storage, copied into an Azure Blob container except for site
/// B's originals, and a second host on the same database is switched to that container —
/// what an operator moving providers does. The check itself is provider-agnostic.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class StorageCheckTests(DatabaseFixture database, AzuriteFixture azurite) : IAsyncLifetime, IClassFixture<AzuriteFixture>
{
    private MediaScenario _local = null!;
    private QuickBuilderApp _switched = null!;
    private HttpClient _admin = null!;
    private string _keyPrefix = null!;
    private string _connection = null!;
    private readonly List<Guid> _siteBFiles = [];

    public async Task InitializeAsync()
    {
        var connection = _connection = await database.CreateDatabaseAsync("storage-check").ConfigureAwait(false);
        _local = await MediaScenario.CreateAsync(database, "storage-check", connectionString: connection).ConfigureAwait(false);

        for (var i = 0; i < 3; i++)
        {
            await MediaApi.UploadOkAsync(_local.Admin, _local.SiteA).ConfigureAwait(false);
        }

        for (var i = 0; i < 2; i++)
        {
            _siteBFiles.Add((await MediaApi.UploadOkAsync(_local.Admin, _local.SiteB).ConfigureAwait(false)).Id);
        }

        _keyPrefix = $"t-{Guid.NewGuid():N}/";
        await CopyAsync(except: _local.SiteB).ConfigureAwait(false);

        _switched = new QuickBuilderApp(connection, settings: azurite.Configuration(_keyPrefix));
        var administrator = await IdentitySeed.SeedUserAsync(_switched.Services, administrator: true, displayName: "Operator").ConfigureAwait(false);
        _admin = await IdentitySeed.CreateSignedInClientAsync(_switched, administrator.Id).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _switched.Dispose();
        _local.Dispose();
        return Task.CompletedTask;
    }

    private StorageConsistencyCheckService StartupCheck => _switched.Services.GetRequiredService<StorageConsistencyCheckService>();

    [Fact]
    public async Task The_startup_check_reports_exactly_the_files_left_out_of_the_copy()
    {
        var check = await StartupCheck.RunOnceAsync(CancellationToken.None);

        Assert.NotNull(check);
        Assert.Equal(StorageCheckStatus.Completed, check.Status);
        Assert.Equal(StorageCheckTrigger.Startup, check.Trigger);
        Assert.Equal("AzureBlob", check.Provider);
        Assert.Equal(5, check.FilesChecked);
        Assert.Equal(2, check.MissingCount);

        var status = await StatusAsync();
        var latest = status.GetProperty("latestCheck");
        Assert.Equal("AzureBlob", status.GetProperty("provider").GetString());
        Assert.Equal(
            _siteBFiles.Order(),
            latest.GetProperty("missing").EnumerateArray().Select(m => m.GetProperty("fileId").GetGuid()).Order());
        Assert.All(latest.GetProperty("missing").EnumerateArray(), m => Assert.Equal(_local.SiteB, m.GetProperty("siteId").GetGuid()));

        // Once per process: a second start-up pass finds the run already done.
        Assert.Null(await StartupCheck.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Hosts_starting_together_run_one_check()
    {
        using var second = new QuickBuilderApp(_connection, settings: azurite.Configuration(_keyPrefix));

        var runs = await Task.WhenAll(
            StartupCheck.RunOnceAsync(CancellationToken.None),
            second.Services.GetRequiredService<StorageConsistencyCheckService>().RunOnceAsync(CancellationToken.None));

        Assert.Single(runs, r => r is not null);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Starting_a_check_runs_it_in_the_background_and_a_second_start_is_refused_while_it_runs()
    {
        await InsertRunningAsync(leaseUntil: DateTimeOffset.UtcNow.AddMinutes(2));

        var refused = await _admin.PostAsync(new Uri("/api/v1/media/storage-check", UriKind.Relative), null);
        await LibraryApi.RefusedAsync(refused, HttpStatusCode.Conflict, "media.storage_check_running");
    }

    [Fact]
    public async Task A_run_left_by_a_crashed_process_is_marked_failed_and_a_new_one_starts()
    {
        var crashed = await InsertRunningAsync(leaseUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        var started = await _admin.PostAsync(new Uri("/api/v1/media/storage-check", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var finished = await WaitForAsync(id);
        Assert.Equal("completed", finished.GetProperty("status").GetString());
        Assert.Equal("admin", finished.GetProperty("trigger").GetString());
        Assert.Equal(2, finished.GetProperty("missingCount").GetInt32());

        await using var scope = _switched.Services.CreateAsyncScope();
        var old = await scope.ServiceProvider.GetRequiredService<MediaDbContext>().StorageChecks.AsNoTracking().SingleAsync(c => c.Id == crashed);
        Assert.Equal(StorageCheckStatus.Failed, old.Status);
    }

    [Fact]
    public async Task The_command_line_exits_3_with_the_ids_then_0_once_the_copy_is_complete()
    {
        var (exit, output) = await RunCommandAsync(_switched, "media", "check-storage");

        Assert.Equal(3, exit);
        foreach (var id in _siteBFiles)
        {
            Assert.Contains($"missing {_local.SiteB:N}/{id:N}", output, StringComparison.Ordinal);
        }

        await CopyAsync(only: _local.SiteB);

        (exit, output) = await RunCommandAsync(_switched, "media", "check-storage");
        Assert.Equal(0, exit);
        Assert.Contains("5 file(s) checked, 0 missing", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_holders_of_the_storage_check_capability_can_read_or_start_it()
    {
        var designer = await IdentitySeed.SeedUserAsync(_switched.Services, siteRoles: [(_local.SiteA, SystemRole.Designer)]);
        using var client = await IdentitySeed.CreateSignedInClientAsync(_switched, designer.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri("/api/v1/media/storage-check", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(new Uri("/api/v1/media/storage-check", UriKind.Relative), null)).StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task The_missing_list_is_capped_at_1000_while_the_count_stays_exact()
    {
        await using (var scope = _switched.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            // 1,003 rows whose originals were never stored, with the two of site B: 1,005 missing.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO media.media_files (id, site_id, file_name, content_type, extension, size_bytes, uploaded_at_utc, uploaded_by,
                    storage_key, search_text, sort_name, search_text_state, metadata_state)
                SELECT g.id, s.site, 'seed.png', 'image/png', 'png', 1, now(), gen_random_uuid(),
                    replace(s.site::text, '-', '') || '/media/' || replace(g.id::text, '-', ''), 'seed.png', 'seed.png', 1, 2
                FROM (SELECT gen_random_uuid() AS id FROM generate_series(1, 1003)) g, (SELECT {_local.SiteA} AS site) s;
                """);
        }

        var check = await StartupCheck.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1_005, check!.MissingCount);
        Assert.Equal(1_000, (await StatusAsync()).GetProperty("latestCheck").GetProperty("missing").GetArrayLength());
    }

    private async Task<JsonElement> StatusAsync()
    {
        var response = await _admin.GetAsync(new Uri("/api/v1/media/storage-check", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> WaitForAsync(Guid id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var latest = (await StatusAsync()).GetProperty("latestCheck");
            if (latest.GetProperty("id").GetGuid() == id && latest.GetProperty("status").GetString() != "running")
            {
                return latest;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("The check did not finish within 10 seconds.");
    }

    private async Task<Guid> InsertRunningAsync(DateTimeOffset leaseUntil)
    {
        await using var scope = _switched.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var check = new StorageCheck(Guid.CreateVersion7(), StorageCheckTrigger.Admin, "AzureBlob", leaseUntil.AddMinutes(-5), leaseUntil, null);
        db.StorageChecks.Add(check);
        await db.SaveChangesAsync();
        return check.Id;
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _switched.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaDbContext>().StorageChecks.CountAsync();
    }

    /// <summary>Copies the Local library into the container, object names unchanged (contracts/storage-providers.md §2).</summary>
    private async Task CopyAsync(Guid? except = null, Guid? only = null)
    {
        var container = azurite.CreateClient();
        foreach (var path in _local.StoredObjects())
        {
            var key = Path.GetRelativePath(_local.App.StoragePath, path).Replace(Path.DirectorySeparatorChar, '/');
            var site = Guid.ParseExact(key[..32], "N");
            if (site == except || (only is not null && site != only))
            {
                continue;
            }

            await using var bytes = File.OpenRead(path);
            await container.GetBlobClient(_keyPrefix + key).UploadAsync(bytes, overwrite: true);
        }
    }

    private static async Task<(int Exit, string Output)> RunCommandAsync(WebApplicationFactory<Program> app, params string[] args)
    {
        var original = Console.Out;
        await using var captured = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Console.SetOut(captured);
        try
        {
            var exit = await HostCommandRunner.TryRunAsync(args, app.Services);
            return (exit ?? -1, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
