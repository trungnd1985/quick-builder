using System.Diagnostics;

namespace QuickBuilder.Release;

/// <summary>Runs <c>git</c> in the repository; the tool's only way of reading history.</summary>
internal static class Git
{
    public static string Run(string repositoryRoot, params string[] args)
    {
        var (exitCode, output, error) = TryRun(repositoryRoot, args);
        return exitCode == 0
            ? output
            : throw new ReleaseRuleException($"git {string.Join(' ', args)} failed: {error.Trim()}");
    }

    public static (int ExitCode, string Output, string Error) TryRun(string repositoryRoot, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new ReleaseRuleException("git could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result, error.Result);
    }

    /// <summary>Whether <paramref name="reference"/> names a commit in this repository.</summary>
    public static bool Exists(string repositoryRoot, string? reference) =>
        !string.IsNullOrWhiteSpace(reference)
        && TryRun(repositoryRoot, "rev-parse", "--verify", "--quiet", reference + "^{commit}").ExitCode == 0;

    /// <summary>
    /// Files that differ between <paramref name="since"/> and the working tree, as
    /// (status letter, path, new path when renamed). Renames are detected.
    /// </summary>
    public static IReadOnlyList<(char Status, string Path, string? NewPath)> Changes(string repositoryRoot, string since, params string[] paths)
    {
        string[] args = ["diff", "--name-status", "-M", since, "--", .. paths];
        return [.. Run(repositoryRoot, args)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0][0], parts[1], parts.Length > 2 ? parts[2] : (string?)null))];
    }

    /// <summary>A file's text at <paramref name="reference"/>, or null when it did not exist there.</summary>
    public static string? Show(string repositoryRoot, string reference, string path)
    {
        var (exitCode, output, _) = TryRun(repositoryRoot, "show", $"{reference}:{path}");
        return exitCode == 0 ? output : null;
    }
}
