using System.Net;
using System.Security.Cryptography;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests.Procedures;

/// <summary>
/// The install and backup/restore pages, run as written against the image under test
/// (026 T024, T049; FR-008, FR-026; SC-001, SC-006; contracts/runnable-snippets.md §3).
/// </summary>
[Trait("Category", "Procedures")]
public sealed class InstallAndBackupProcedureTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string> ReleaseCompose = new()
    {
        ["release-compose"] = ProcedureRunner.ComposeFile,
    };

    [SkippableFact]
    [Trait("Procedure", "install")]
    public async Task The_install_page_ends_with_a_working_installation()
    {
        Skip.IfNot(await ProcedureRunner.AvailableAsync(), "Docker, bash or QB_SMOKE_IMAGE is not available.");
        await using var runner = new ProcedureRunner("install");

        output.WriteLine(await runner.RunProcedureAsync("install", runner.Root, ReleaseCompose));

        using var client = new HttpClient { BaseAddress = runner.Direct };
        var api = new OperatorApi(client);
        await api.WaitForReadyAsync();
        Assert.False(File.ReadAllText(Path.Combine(runner.InstallDirectory, ".env")).Contains("change-me", StringComparison.Ordinal));

        await api.SetUpAsync(await runner.SetupCodeAsync(runner.InstallDirectory));
        await api.CreatePublishedSiteAsync("Install", "install.test", "Installed from the docs");
        var (status, body) = await OperatorApi.PublicAsync(client, "install.test", "/");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Installed from the docs</h1>", System.Text.Encoding.UTF8.GetString(body), StringComparison.Ordinal);
    }

    [SkippableFact]
    [Trait("Procedure", "backup-restore")]
    public async Task A_documented_backup_restores_onto_a_fresh_installation()
    {
        Skip.IfNot(await ProcedureRunner.AvailableAsync(), "Docker, bash or QB_SMOKE_IMAGE is not available.");
        await using var runner = new ProcedureRunner("backup");

        // An installation with a published page, an unpublished draft and an uploaded file.
        await runner.RunProcedureAsync("install", runner.Root, ReleaseCompose);
        using var client = new HttpClient { BaseAddress = runner.Direct };
        var api = new OperatorApi(client);
        await api.WaitForReadyAsync();
        await api.SetUpAsync(await runner.SetupCodeAsync(runner.InstallDirectory));
        var site = await api.CreatePublishedSiteAsync("Backed up", "backup.test", "Before the backup");
        await api.SaveHomeDraftAsync(site, "An unpublished draft");
        var file = await api.UploadAsync(site.SiteId, OperatorApi.Png, "image/png", "kept.png");
        var draftBefore = await api.HomeDraftAsync(site);

        output.WriteLine(await runner.RunProcedureAsync("backup", runner.InstallDirectory));

        // The original is gone, volumes and all, before anything is restored.
        var down = await runner.ComposeAsync(runner.InstallDirectory, "down", "-v");
        Assert.True(down.ExitCode == 0, down.Output);

        output.WriteLine(await runner.RunProcedureAsync("restore", runner.InstallDirectory));
        runner.TrackProject(Path.Combine(runner.Root, "quickbuilder-restore"));

        using var restored = new HttpClient { BaseAddress = runner.Direct };
        var after = new OperatorApi(restored);
        await after.WaitForReadyAsync();
        await after.SignInAsync();

        var (status, page) = await OperatorApi.PublicAsync(restored, "backup.test", "/");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Before the backup</h1>", System.Text.Encoding.UTF8.GetString(page), StringComparison.Ordinal);
        Assert.Equal(draftBefore, await after.HomeDraftAsync(site));

        var (fileStatus, bytes) = await OperatorApi.PublicAsync(restored, "backup.test", file);
        Assert.Equal(HttpStatusCode.OK, fileStatus);
        Assert.Equal(SHA256.HashData(OperatorApi.Png), SHA256.HashData(bytes));
    }
}
