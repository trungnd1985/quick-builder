using System.Diagnostics;

namespace QuickBuilder.UnitTests.Release;

/// <summary>A throwaway git repository for the release tool's tests.</summary>
internal sealed class TempRepository : IDisposable
{
    public TempRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "qb-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "release@example.test");
        Git("config", "user.name", "Release Test");
        Git("config", "commit.gpgsign", "false");
        Git("config", "core.autocrlf", "false");
    }

    public string Root { get; }

    /// <summary>Writes <paramref name="text"/> at <paramref name="path"/> (relative, forward slashes).</summary>
    public TempRepository Write(string path, string text)
    {
        var full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return this;
    }

    public TempRepository Delete(string path)
    {
        File.Delete(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
        return this;
    }

    public TempRepository Move(string from, string to)
    {
        Git("mv", from, to);
        return this;
    }

    public TempRepository Commit(string message = "change")
    {
        Git("add", "-A");
        Git("commit", "-q", "--allow-empty", "-m", message);
        return this;
    }

    public TempRepository Tag(string name)
    {
        Git("tag", name);
        return this;
    }

    /// <summary>A minimal Directory.Build.props with the version properties the tool reads.</summary>
    public TempRepository Props(string versionPrefix, string floor = "0.0.0") =>
        Write("Directory.Build.props", $"""
            <Project>
              <PropertyGroup>
                <VersionPrefix>{versionPrefix}</VersionPrefix>
                <VersionSuffix>dev</VersionSuffix>
                <QbUpgradeFloor>{floor}</QbUpgradeFloor>
              </PropertyGroup>
            </Project>
            """);

    public string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}");
        }

        return output;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary repository is harmless.
        }
    }
}
