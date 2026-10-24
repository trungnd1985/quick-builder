using System.Globalization;
using Xunit;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>
/// A release's database snapshot: the database dump, the media files and the expectations
/// (019 contracts/snapshot.md §1, data-model §3).
/// </summary>
/// <remarks>
/// Dump and restore follow the steps the operations guide gives operators for backing up and
/// rolling back, so the gate exercises the procedure it tells them to trust (FR-035, FR-036).
/// Binary files never pass through a process's text output: <c>pg_dump</c> writes inside the
/// database container and <c>docker compose cp</c> copies the file out.
/// </remarks>
internal sealed class SnapshotBundle(string directory, string version)
{
    /// <summary>A throwaway image with a shell and tar; the database image is already pulled.</summary>
    private const string ToolImage = "postgres:16-alpine";

    private const string MediaPath = "/var/lib/quickbuilder/media";

    public string Directory { get; } = directory;

    public string Version { get; } = version;

    public string DumpFile => Path.Combine(Directory, $"quickbuilder-{Version}-snapshot.dump");

    public string MediaFile => Path.Combine(Directory, $"quickbuilder-{Version}-media.tar.gz");

    public string ExpectationsFile => Path.Combine(Directory, $"quickbuilder-{Version}-expectations.json");

    public bool Exists => File.Exists(DumpFile) && File.Exists(MediaFile) && File.Exists(ExpectationsFile);

    public Expectations ReadExpectations() => Expectations.FromJson(File.ReadAllText(ExpectationsFile));

    /// <summary>
    /// Stops the application (a consistent dump), dumps the database and archives the media
    /// volume, and writes <paramref name="expectations"/> beside them.
    /// </summary>
    public async Task DumpAsync(ComposeDeployment deployment, Expectations expectations)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(expectations);
        System.IO.Directory.CreateDirectory(Directory);

        await Must(deployment.ComposeAsync("stop app"), "stopping the application").ConfigureAwait(false);

        await Must(
            deployment.ComposeAsync("exec -T db pg_dump -U quickbuilder -d quickbuilder --format=custom --no-owner --no-acl -f /tmp/snapshot.dump"),
            "pg_dump").ConfigureAwait(false);
        await Must(deployment.ComposeAsync($"cp db:/tmp/snapshot.dump \"{DumpFile}\""), "copying the dump out").ConfigureAwait(false);

        await Must(
            deployment.RunAsync("docker", $"run --rm -v {MediaVolume}:/data:ro -v \"{Directory}\":/out {ToolImage} tar -czf /out/{Path.GetFileName(MediaFile)} -C /data ."),
            "archiving the media volume").ConfigureAwait(false);

        await File.WriteAllTextAsync(ExpectationsFile, expectations.ToJson()).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the deployment's database and media with this snapshot: stop everything,
    /// remove both volumes, start the database alone, restore, and unpack the media owned by
    /// the image's own user. The application is left stopped; the caller starts the image it
    /// wants to test.
    /// </summary>
    public async Task RestoreAsync(ComposeDeployment deployment, string appImage)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        Assert.True(Exists, $"The snapshot of {Version} is incomplete in {Directory}.");

        await Must(deployment.ComposeAsync("down -v"), "removing the deployment").ConfigureAwait(false);
        await Must(deployment.ComposeAsync("up -d --wait db"), "starting the database").ConfigureAwait(false);

        await Must(deployment.ComposeAsync($"cp \"{DumpFile}\" db:/tmp/snapshot.dump"), "copying the dump in").ConfigureAwait(false);
        await Must(
            deployment.ComposeAsync("exec -T db pg_restore -U quickbuilder -d quickbuilder --no-owner --role=quickbuilder --exit-on-error /tmp/snapshot.dump"),
            "pg_restore").ConfigureAwait(false);

        // The volume exists once the app container is created; the owner is whoever the
        // image runs as, so new uploads after the restore can still be written.
        await Must(deployment.ComposeAsync("create app"), "creating the application container").ConfigureAwait(false);
        var owner = (await Must(deployment.RunAsync("docker", $"run --rm --quiet --entrypoint id {appImage} -u"), "reading the image's user").ConfigureAwait(false)).Trim();
        var group = (await Must(deployment.RunAsync("docker", $"run --rm --quiet --entrypoint id {appImage} -g"), "reading the image's group").ConfigureAwait(false)).Trim();
        _ = int.Parse(owner, CultureInfo.InvariantCulture);
        _ = int.Parse(group, CultureInfo.InvariantCulture);

        await Must(
            deployment.RunAsync("docker", $"run --rm -v {MediaVolume}:/data -v \"{Directory}\":/in:ro {ToolImage} sh -c \"tar -xzf /in/{Path.GetFileName(MediaFile)} -C /data && chown -R {owner}:{group} /data\""),
            "restoring the media volume").ConfigureAwait(false);
    }

    /// <summary>The compose project's media volume (docker-compose.yml, <c>media-data</c>).</summary>
    private static string MediaVolume => $"{ComposeDeployment.ProjectName}_media-data";

    private static async Task<string> Must(Task<(int ExitCode, string Output)> step, string what)
    {
        var (exitCode, output) = await step.ConfigureAwait(false);
        Assert.True(exitCode == 0, $"{what} failed ({exitCode}):\n{output}");
        return output;
    }
}
