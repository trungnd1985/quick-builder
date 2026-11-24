using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace QuickBuilder.SmokeTests.Procedures;

/// <summary>
/// Runs a procedure from the operator documentation exactly as written, apart from the closed
/// list of substitutions (026 T023; research OD-12; contracts/runnable-snippets.md).
/// </summary>
/// <remarks>
/// <para>
/// Each runner owns a scratch directory, the way an operator owns a server: the install
/// procedure creates <c>quickbuilder/</c> in it, and later procedures run inside that
/// directory, as the pages say. Compose projects get a name of their own
/// (<c>COMPOSE_PROJECT_NAME</c>), so a run never touches another installation, and the app
/// listens on a free port (<c>QUICKBUILDER_PORT</c>). Both are environment variables the
/// documented commands already honour; nothing in the pages changes for them.
/// </para>
/// <para>
/// Requires the image under test in <c>QB_SMOKE_IMAGE</c>. Skips without Docker or bash,
/// unless <c>QB_SMOKE_REQUIRE_DOCKER</c> is set, as in CI.
/// </para>
/// </remarks>
internal sealed class ProcedureRunner : IAsyncDisposable
{
    private readonly List<string> _projectDirectories = [];

    public ProcedureRunner(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), "qb-procedures", $"{name}-{Guid.NewGuid():N}"[..Math.Min(name.Length + 13, 40)]);
        Directory.CreateDirectory(Root);
        Project = $"qbdocs{Guid.NewGuid():N}"[..14];
        Port = FreePort();
    }

    /// <summary>The scratch directory, standing in for the operator's server.</summary>
    public string Root { get; }

    /// <summary>The installation directory the install page creates.</summary>
    public string InstallDirectory => Path.Combine(Root, "quickbuilder");

    /// <summary>The Compose project name every procedure of this run uses.</summary>
    public string Project { get; private set; }

    /// <summary>The host port the app listens on (127.0.0.1).</summary>
    public int Port { get; }

    public static string? Image => System.Environment.GetEnvironmentVariable("QB_SMOKE_IMAGE") is { Length: > 0 } image ? image : null;

    public static bool Required => System.Environment.GetEnvironmentVariable("QB_SMOKE_REQUIRE_DOCKER") == "1";

    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    /// <summary>Whether this machine can run procedures at all: Docker, bash and an image.</summary>
    public static async Task<bool> AvailableAsync()
    {
        var docker = await RunAsync("docker", ["info", "--format", "{{.ServerVersion}}"], RepositoryRoot, new Dictionary<string, string>()).ConfigureAwait(false);
        var bash = await RunAsync(Bash, ["-c", "echo ok"], RepositoryRoot, new Dictionary<string, string>()).ConfigureAwait(false);
        var available = docker.ExitCode == 0 && bash.ExitCode == 0 && Image is not null;
        if (!available && Required)
        {
            throw new InvalidOperationException(
                $"QB_SMOKE_REQUIRE_DOCKER is set, but docker ({docker.ExitCode}), bash ({bash.ExitCode}) or QB_SMOKE_IMAGE ({Image ?? "unset"}) is missing.");
        }

        return available;
    }

    /// <summary>The release compose file under test: the repository's, which names the image through QUICKBUILDER_IMAGE.</summary>
    public static string ComposeFile => Path.Combine(RepositoryRoot, "docker-compose.yml");

    /// <summary>
    /// Writes <paramref name="procedure"/> from the docs with <c>qb-docs snippets write</c> and runs
    /// its <c>run.sh</c> in <paramref name="workingDirectory"/>. Fails the test with the script's
    /// output when it does not exit 0.
    /// </summary>
    public async Task<string> RunProcedureAsync(string procedure, string workingDirectory, IReadOnlyDictionary<string, string>? substitutions = null)
    {
        var scripts = Path.Combine(Root, $".{procedure}");
        var output = new StringWriter();
        var error = new StringWriter();
        var written = QuickBuilder.Docs.Snippets.SnippetCommands.Write(
            RepositoryRoot, procedure, scripts, substitutions ?? new Dictionary<string, string>(), output, error);
        Assert.True(written == 0, $"qb-docs snippets write --procedure {procedure} failed:\n{error}");

        // Files the procedure writes (proxy configuration) belong in the working directory.
        Directory.CreateDirectory(workingDirectory);
        foreach (var file in Directory.EnumerateFiles(scripts, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "run.sh"))
        {
            var target = Path.Combine(workingDirectory, Path.GetRelativePath(scripts, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        TrackProject(workingDirectory);
        var run = await RunAsync(Bash, ["-s"], workingDirectory, Environment(), await File.ReadAllTextAsync(Path.Combine(scripts, "run.sh")).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.True(run.ExitCode == 0, $"The documented {procedure} procedure failed (exit {run.ExitCode}):\n{run.Output}");
        return run.Output;
    }

    /// <summary>Runs <c>docker compose</c> in an installation directory, with this run's environment.</summary>
    public async Task<(int ExitCode, string Output)> ComposeAsync(string directory, params string[] arguments) =>
        await RunAsync("docker", ["compose", .. arguments], directory, Environment()).ConfigureAwait(false);

    /// <summary>The setup code from the app's logs, as an operator reads it.</summary>
    public async Task<string> SetupCodeAsync(string directory)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var logs = await ComposeAsync(directory, "logs", "--no-log-prefix", "app").ConfigureAwait(false);
            var line = logs.Output.Split('\n').FirstOrDefault(l => l.Contains("\"SetupCode\"", StringComparison.Ordinal));
            if (line is not null)
            {
                using var document = JsonDocument.Parse(line.Trim());
                return document.RootElement.GetProperty("SetupCode").GetString()!;
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The setup code never appeared in the application's logs.");
    }

    /// <summary>The address the app answers on directly, bypassing any proxy.</summary>
    public Uri Direct => new($"http://127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>A free TCP port on 127.0.0.1.</summary>
    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var directory in _projectDirectories.Where(Directory.Exists).Distinct())
        {
            if (Environment().TryGetValue("QB_SMOKE_LOG_DIR", out var logs) && logs.Length > 0)
            {
                var captured = await ComposeAsync(directory, "logs", "--no-color").ConfigureAwait(false);
                Directory.CreateDirectory(logs);
                await File.WriteAllTextAsync(Path.Combine(logs, $"{Project}-{Path.GetFileName(directory)}.log"), captured.Output).ConfigureAwait(false);
            }

            await ComposeAsync(directory, "down", "-v", "--remove-orphans").ConfigureAwait(false);
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Volumes mounted from the scratch directory may still be held; a leftover temp
            // directory is harmless.
        }
    }

    /// <summary>Remembers a directory that holds a Compose project, so disposal stops it.</summary>
    public void TrackProject(string directory) => _projectDirectories.Add(directory);

    private Dictionary<string, string> Environment()
    {
        var environment = new Dictionary<string, string>
        {
            ["COMPOSE_PROJECT_NAME"] = Project,
            ["QUICKBUILDER_PORT"] = Port.ToString(CultureInfo.InvariantCulture),
            ["MSYS_NO_PATHCONV"] = "1",
        };
        if (Image is { } image)
        {
            environment["QUICKBUILDER_IMAGE"] = image;
        }

        if (System.Environment.GetEnvironmentVariable("QB_SMOKE_LOG_DIR") is { Length: > 0 } logs)
        {
            environment["QB_SMOKE_LOG_DIR"] = logs;
        }

        return environment;
    }

    /// <summary>
    /// bash: on Windows, Git Bash when installed, because the bash on the path may be WSL's, which
    /// cannot read Windows paths. The documented commands are for Linux and macOS (or WSL).
    /// </summary>
    private static string Bash { get; } =
        OperatingSystem.IsWindows() && File.Exists(@"C:\Program Files\Git\bin\bash.exe") ? @"C:\Program Files\Git\bin\bash.exe" : "bash";

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string file, IReadOnlyList<string> arguments, string directory, IReadOnlyDictionary<string, string> environment, string? input = null)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        try
        {
            using var process = Process.Start(start)!;
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.Replace("\r\n", "\n", StringComparison.Ordinal)).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, $"{file} could not be started: {e.Message}");
        }
    }
}
