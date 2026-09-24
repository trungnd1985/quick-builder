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
    public const string BaseAddress = "http://127.0.0.1:8080";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly string _files = string.Concat(
        new[] { "docker-compose.yml" }.Concat(overrideFiles).Select(f => $"-f {f} "));

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
            return false;
        }

        // Start from nothing, including the volume: SC-001 is a claim about a *fresh*
        // installation, and a leftover database would let a broken migration path pass.
        await ComposeAsync("down -v").ConfigureAwait(false);

        var build = await ComposeAsync("build").ConfigureAwait(false);
        Assert.True(build.ExitCode == 0, $"docker compose build failed:\n{build.Output}");

        SinceUp.Start();
        var up = await ComposeAsync("up -d").ConfigureAwait(false);
        Assert.True(up.ExitCode == 0, $"docker compose up failed:\n{up.Output}");
        return true;
    }

    public async Task StopAsync()
    {
        if (DockerAvailable)
        {
            await ComposeAsync("down -v").ConfigureAwait(false);
        }
    }

    /// <summary>Runs <c>docker compose</c> with this deployment's files.</summary>
    public Task<(int ExitCode, string Output)> ComposeAsync(string arguments) =>
        RunAsync("docker", $"compose {_files}{arguments}");

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
            },
        };

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
