using QuickBuilder.Docs.Lint;
using QuickBuilder.Docs.Publishing;
using QuickBuilder.Docs.Settings;
using QuickBuilder.Docs.Snippets;
using QuickBuilder.Docs.UpgradeNotes;

namespace QuickBuilder.Docs;

/// <summary>
/// <c>qb-docs</c>: generates and checks the operator documentation (026 contracts/qb-docs-cli.md).
/// </summary>
/// <remarks>
/// Exit codes: 0 when everything passes, 1 when a check finds a problem (one line per finding,
/// <c>&lt;where&gt;: &lt;problem&gt; — &lt;fix&gt;</c>), 2 for a usage or environment error.
/// </remarks>
internal static class Program
{
    internal const int Pass = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;

    internal const string GenerateCommand = "dotnet run --project tools/QuickBuilder.Docs -- settings generate";

    private const string Usage = """
        Usage: qb-docs <command> [options]
          settings generate [--out <path>]
          settings check
          settings keys
          upgrade-notes generate [--through X.Y.Z]
          lint [--check <name>]
          snippets list [--procedure <name>]
          snippets write --procedure <name> --dir <path> [--substitute name=value ...]
          publish prepare --site <gh-pages checkout> --build <dist> --version X.Y.Z --highest true|false
        """;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error, RepositoryRoot.Find(Directory.GetCurrentDirectory()));

    internal static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            var (command, options, substitutions) = Parse(args);
            return command switch
            {
                "settings generate" => SettingsCommands.Generate(repositoryRoot, options.GetValueOrDefault("out"), output, error),
                "settings check" => SettingsCommands.Check(repositoryRoot, output, error),
                "settings keys" => SettingsCommands.Keys(repositoryRoot, output, error),
                "upgrade-notes generate" => UpgradeNotesGenerator.Run(repositoryRoot, options.GetValueOrDefault("through"), output, error),
                "lint" => LintRunner.Run(repositoryRoot, options.GetValueOrDefault("check"), output, error),
                "snippets list" => SnippetCommands.List(repositoryRoot, options.GetValueOrDefault("procedure"), output, error),
                "snippets write" => SnippetCommands.Write(
                    repositoryRoot, Required(options, "procedure"), Required(options, "dir"), substitutions, output, error),
                "publish prepare" => VersionsManifest.Prepare(
                    Required(options, "site"), Required(options, "build"), Required(options, "version"),
                    bool.Parse(Required(options, "highest")), DateOnly.FromDateTime(DateTime.UtcNow), output, error),
                _ => throw new UsageException($"unknown command '{command}'."),
            };
        }
        catch (UsageException e)
        {
            error.WriteLine($"qb-docs: {e.Message}");
            error.WriteLine(Usage);
            return UsageError;
        }
    }

    private static (string Command, Dictionary<string, string> Options, Dictionary<string, string> Substitutions) Parse(IReadOnlyList<string> args)
    {
        var words = args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (words.Count == 0)
        {
            throw new UsageException("no command.");
        }

        var command = words[0] is "lint" ? "lint" : string.Join(' ', words.Take(2));
        if (words.Count > (command == "lint" ? 1 : 2))
        {
            throw new UsageException($"unexpected '{words[^1]}'.");
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = words.Count; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
            {
                throw new UsageException($"'{args[i]}' needs a value.");
            }

            var name = args[i][2..];
            if (name == "substitute")
            {
                var pair = args[i + 1].Split('=', 2);
                if (pair.Length != 2)
                {
                    throw new UsageException($"--substitute takes name=value, not '{args[i + 1]}'.");
                }

                substitutions[pair[0]] = pair[1];
            }
            else
            {
                options[name] = args[i + 1];
            }
        }

        return (command, options, substitutions);
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new UsageException($"--{name} is required.");
}

/// <summary>A command line the tool cannot run.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Finds the repository root from a directory inside it.</summary>
internal static class RepositoryRoot
{
    public static string Find(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new UsageException("run qb-docs inside the repository (no QuickBuilder.slnx found above the current directory).");
    }
}
