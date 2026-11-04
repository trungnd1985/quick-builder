using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// An import that fails, is cancelled, or loses its connection after every part has applied
/// leaves the installation exactly as it was (020 FR-018, SC-004; research ST-05).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ImportFailureTests(DatabaseFixture database)
{
    public enum Fault
    {
        Throw,
        Cancel,
        KillConnection,
    }

    [Theory]
    [InlineData(Fault.Throw, "failed")]
    [InlineData(Fault.Cancel, "cancelled")]
    [InlineData(Fault.KillConnection, "failed")]
    public async Task A_fault_after_every_part_applied_leaves_no_site_and_no_row(Fault fault, string state)
    {
        using var site = await FullSite.CreateAsync(database, $"import-fault-{fault}".ToLowerInvariant()).ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        var before = await CountsAsync(site.App).ConfigureAwait(true);

        // A second instance on the same database and storage, whose last part fails as asked.
        Guid? transferId = null;
        using var importer = new QuickBuilderApp(
            site.App.ConnectionString,
            storagePath: site.App.StoragePath,
            configureTestServices: services => services.AddScoped<ISiteArchivePart>(_ => new FaultPart(async (context, ct) =>
            {
                switch (fault)
                {
                    case Fault.Throw:
                        throw new InvalidOperationException("A part failed after the others had written.");
                    case Fault.Cancel:
                        Assert.Equal(HttpStatusCode.Accepted, (await TransferApi.CancelAsync(site.Admin, transferId!.Value).ConfigureAwait(false)).StatusCode);
                        await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                        break;
                    case Fault.KillConnection:
                        await KillAsync(site.App.ConnectionString, (NpgsqlConnection)context.Session.Connection).ConfigureAwait(false);
                        break;
                }
            })));

        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        transferId = id;
        var checkedTransfer = await TransferApi.CheckAsync(importer, site.Admin, id, "new_site").ConfigureAwait(true);
        await PagesApi.OkAsync(await TransferApi.ConfirmAsync(site.Admin, id, new
        {
            newSiteName = "Doomed", newSiteHostName = "doomed.test", acceptWarnings = true,
            checkFingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString(),
        }).ConfigureAwait(true), HttpStatusCode.Accepted).ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(importer).ConfigureAwait(true);

        var done = await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true);
        Assert.Equal(state, done.GetProperty("state").GetString());
        Assert.Equal(JsonNull, done.GetProperty("siteId").ValueKind);
        Assert.Equal(before, await CountsAsync(site.App).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.NotFound, (await site.App.CreateClientForHost("doomed.test").GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    private const System.Text.Json.JsonValueKind JsonNull = System.Text.Json.JsonValueKind.Null;

    /// <summary>Rows of every table the parts write, across all sites.</summary>
    private static async Task<string> CountsAsync(QuickBuilderApp app)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        var counts = new List<string>();
        foreach (var table in new[]
        {
            "sites.sites", "sites.site_host_names", "media.media_files", "media.folders", "editor.design_token_sets", "editor.design_versions",
            "cms.collections", "cms.items", "cms.item_versions", "pages.pages", "pages.page_drafts", "pages.components", "pages.menus",
            "pages.published_versions", "pages.publications", "pages.redirects", "pages.page_trees", "pages.live_dependencies", "pages.component_usages", "cms.item_slugs", "cms.item_references",
        })
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
            counts.Add($"{table}={await command.ExecuteScalarAsync().ConfigureAwait(false)}");
        }

        return string.Join(", ", counts);
    }

    private static async Task KillAsync(string connectionString, NpgsqlConnection victim)
    {
        await using var pidCommand = new NpgsqlCommand("SELECT pg_backend_pid()", victim);
        var pid = (int)(await pidCommand.ExecuteScalarAsync().ConfigureAwait(false))!;
        await using var killer = new NpgsqlConnection(connectionString);
        await killer.OpenAsync().ConfigureAwait(false);
        await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", killer);
        kill.Parameters.AddWithValue("pid", pid);
        await kill.ExecuteScalarAsync().ConfigureAwait(false);
    }

    /// <summary>A part that runs after Pages (50) and fails during the apply.</summary>
    private sealed class FaultPart(Func<PartApplyContext, CancellationToken, Task> fault) : ISiteArchivePart
    {
        public string Name => "fault";

        public int Order => 60;

        public int Version => 1;

        public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>();

        public Task ExportAsync(PartExportContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken) => fault(context, cancellationToken);
    }
}
