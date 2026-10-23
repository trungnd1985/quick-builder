using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Xunit;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// Every class that starts the reference deployment. They share one compose project and
/// one published port, so they must never run at the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DeploymentGroup
{
    public const string Name = "Reference deployment";
}

/// <summary>
/// The reference compose deployment, driven the way an operator drives it: build, start,
/// read the logs, run commands in the container.
/// </summary>
/// <param name="overrideFiles">
/// Extra compose files layered over <c>docker-compose.yml</c>, such as
/// <c>docker-compose.perf.yml</c>.
/// </param>
internal sealed class ComposeDeployment(params string[] overrideFiles)
{
    /// <summary>
    /// The compose project the tests run as. Never the default: that is the operator's own
    /// deployment of this checkout (<c>quick-builder</c>), and <c>down -v</c> below would delete
    /// its database and media volumes.
    /// </summary>
    public const string ProjectName = "quickbuilder-smoke";

    /// <summary>Published on its own port, so it can run while a real deployment holds 8080.</summary>
    public const int Port = 18080;

    public const string BaseAddress = "http://127.0.0.1:18080";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The published image to run instead of building this checkout (019 research RL-10):
    /// the release workflow sets it to the candidate's digest, so the gates test exactly the
    /// bytes operators will pull. Null means build.
    /// </summary>
    public static string? PublishedImage { get; } =
        Environment.GetEnvironmentVariable("QB_SMOKE_IMAGE") is { Length: > 0 } image ? image : null;

    /// <summary>
    /// The reference file, then either nothing (a published image) or the build override, then
    /// the caller's overrides. The build override comes before them so they still win.
    /// </summary>
    private readonly string _files = string.Concat(
        new[] { "docker-compose.yml" }
            .Concat(PublishedImage is null ? ["docker-compose.build.yml"] : [])
            .Concat(overrideFiles)
            .Select(f => $"-f {f} "));

    /// <summary>What ran: the published image's repository digest, or the local build.</summary>
    public string? RanImage { get; private set; }

    public string RepositoryRoot { get; } = FindRepositoryRoot();

    public bool DockerAvailable { get; private set; }

    /// <summary>Time since <c>compose up</c> was issued (SC-001 evidence).</summary>
    public Stopwatch SinceUp { get; } = new();

    /// <summary>
    /// Builds the image and starts a fresh deployment. Returns false, starting nothing,
    /// when Docker is unavailable.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        DockerAvailable = (await RunAsync("docker", "info").ConfigureAwait(false)).ExitCode == 0;
        if (!DockerAvailable)
        {
            // 012 FR-041: CI sets this, so a runner without Docker fails the build instead of
            // passing it by skipping every smoke and performance test.
            Assert.False(
                Environment.GetEnvironmentVariable("QB_SMOKE_REQUIRE_DOCKER") == "1",
                "QB_SMOKE_REQUIRE_DOCKER is set, but 'docker info' failed.");
            return false;
        }

        // Start from nothing, including the volume: SC-001 is a claim about a *fresh*
        // installation, and a leftover database would let a broken migration path pass.
        await ComposeAsync("down -v").ConfigureAwait(false);

        if (PublishedImage is null)
        {
            var build = await ComposeAsync("build").ConfigureAwait(false);
            Assert.True(build.ExitCode == 0, $"docker compose build failed:\n{build.Output}");
            RanImage = "quickbuilder:local (built from this checkout)";
        }
        else
        {
            var pull = await ComposeAsync("pull app").ConfigureAwait(false);
            Assert.True(pull.ExitCode == 0, $"docker compose pull failed for {PublishedImage}:\n{pull.Output}");
            var digest = await RunAsync("docker", $"image inspect --format \"{{{{index .RepoDigests 0}}}}\" {PublishedImage}").ConfigureAwait(false);
            RanImage = digest.ExitCode == 0 ? digest.Output.Trim() : PublishedImage;
        }

        SinceUp.Start();
        var up = await ComposeAsync("up -d").ConfigureAwait(false);
        Assert.True(up.ExitCode == 0, $"docker compose up failed:\n{up.Output}");
        return true;
    }

    public async Task StopAsync()
    {
        if (DockerAvailable)
        {
            // CI keeps each deployment's logs, since they are gone once it is torn down.
            if (Environment.GetEnvironmentVariable("QB_SMOKE_LOG_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                var logs = await ComposeAsync("logs --no-color").ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(directory, $"compose-{DateTime.UtcNow:yyyyMMddHHmmssfff}.log"), logs.Output).ConfigureAwait(false);
            }

            await ComposeAsync("down -v").ConfigureAwait(false);
        }
    }

    /// <summary>Runs <c>docker compose</c> with this deployment's files.</summary>
    public Task<(int ExitCode, string Output)> ComposeAsync(string arguments) =>
        RunAsync("docker", $"compose -p {ProjectName} {_files}{arguments}");

    /// <summary>
    /// Finds the setup code in the container's log stream, as an operator would with
    /// <c>docker compose logs app</c> (002 research R-09). Logs are compact JSON, one event
    /// per line; the code is the <c>SetupCode</c> property of the one event that has it.
    /// </summary>
    public async Task<string> ReadSetupCodeFromLogsAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var logs = await ComposeAsync("logs --no-log-prefix app").ConfigureAwait(false);
            var line = logs.Output
                .Split('\n')
                .FirstOrDefault(l => l.Contains("\"SetupCode\"", StringComparison.Ordinal));

            if (line is not null)
            {
                using var document = JsonDocument.Parse(line.Trim());
                return document.RootElement.GetProperty("SetupCode").GetString()!;
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The setup code never appeared in the application's logs.");
    }

    public async Task<TimeSpan> WaitForReadyAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var started = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - started < ReadyTimeout)
        {
            try
            {
                var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return DateTimeOffset.UtcNow - started;
                }
            }
            catch (HttpRequestException)
            {
                // The container is still starting; the port is not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        var logs = await ComposeAsync("logs --tail 40 app").ConfigureAwait(false);
        throw new InvalidOperationException(
            $"The application never reported ready within {ReadyTimeout}. Recent logs:\n{logs.Output}");
    }

    public async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Environment = { ["QUICKBUILDER_PORT"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
        };

        // Compose reads the image to run from QUICKBUILDER_IMAGE (docker-compose.yml); in
        // build mode the override names its own.
        if (PublishedImage is not null)
        {
            process.StartInfo.Environment["QUICKBUILDER_IMAGE"] = PublishedImage;
        }

        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
