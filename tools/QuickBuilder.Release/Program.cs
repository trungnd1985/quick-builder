using System.Text.Json;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Release;

/// <summary>
/// <c>qb-release</c>: the release rules that must be right, in one tested place (019 research
/// RL-22, contracts/release-pipeline.md §5).
/// </summary>
/// <remarks>
/// Exit codes: 0 when the check passes, 1 when a rule fails (the message names the file,
/// section or version), 2 for a usage error. Results meant for the workflow are printed as
/// <c>key=value</c> lines, ready for <c>$GITHUB_OUTPUT</c>.
/// </remarks>
internal static class Program
{
    internal const int Pass = 0;
    internal const int RuleFailed = 1;
    internal const int UsageError = 2;

    private const string Usage = """
        Usage: qb-release <command> [options]
          version check --tag vX.Y.Z[-rc.N] [--available "0.1.0 0.2.0"]
          upgrade-sources --version X.Y.Z [--available "..."]
          migrations check [--since <tag>]
          patch check --since <tag>
        """;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error, Directory.GetCurrentDirectory());

    internal static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            var (command, options) = Parse(args);
            switch (command)
            {
                case "version check":
                    {
                        var available = Available(options);
                        var result = ReleaseVersions.Check(Required(options, "tag"), available, BuildProps.Read(repositoryRoot));
                        var highest = !available.Any(v => !v.IsPreRelease && v > result.Version);
                        output.WriteLine($"version={result.Version}");
                        output.WriteLine($"highest={(highest && result.Kind != ReleaseKind.PreRelease ? "true" : "false")}");
                        output.WriteLine($"minor_base=v{result.Version.Major}.{result.Version.Minor}.0");
                        output.WriteLine($"kind={result.Kind.ToString().ToLowerInvariant()}");
                        output.WriteLine($"previous={result.Previous?.ToString() ?? "none"}");
                        output.WriteLine($"prerelease={(result.Kind == ReleaseKind.PreRelease ? "true" : "false")}");
                        return Pass;
                    }

                case "upgrade-sources":
                    {
                        var sources = UpgradeSources.Compute(SemanticVersion.Parse(Required(options, "version")), Available(options));
                        output.WriteLine($"sources={JsonSerializer.Serialize(sources)}");
                        return Pass;
                    }

                case "migrations check":
                    {
                        var result = MigrationFreeze.Check(repositoryRoot, options.GetValueOrDefault("since"));
                        if (result.Notice is { } notice)
                        {
                            output.WriteLine($"::notice::{notice}");
                        }

                        return Report(result.Problems, output, error, "Shipped migrations are unchanged.");
                    }

                case "patch check":
                    return Report(PatchRules.Check(repositoryRoot, Required(options, "since")), output, error, "Nothing in this patch changes stored data.");

                default:
                    error.WriteLine($"Unknown command '{command}'.");
                    error.WriteLine(Usage);
                    return UsageError;
            }
        }
        catch (UsageException ex)
        {
            error.WriteLine(ex.Message);
            error.WriteLine(Usage);
            return UsageError;
        }
        catch (ReleaseRuleException ex)
        {
            error.WriteLine($"::error::{ex.Message}");
            return RuleFailed;
        }
        catch (FormatException ex)
        {
            error.WriteLine($"::error::{ex.Message}");
            return RuleFailed;
        }
    }

    private static int Report(IReadOnlyList<string> problems, TextWriter output, TextWriter error, string success)
    {
        if (problems.Count == 0)
        {
            output.WriteLine(success);
            return Pass;
        }

        foreach (var problem in problems)
        {
            error.WriteLine($"::error::{problem}");
        }

        return RuleFailed;
    }

    /// <summary>The command (one or two words) and its <c>--name value</c> options.</summary>
    private static (string Command, Dictionary<string, string> Options) Parse(IReadOnlyList<string> args)
    {
        var words = args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (words.Count == 0)
        {
            throw new UsageException("No command given.");
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = words.Count; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
            {
                throw new UsageException($"Option '{args[i]}' needs a value.");
            }

            options[args[i][2..]] = args[i + 1];
        }

        return (string.Join(' ', words), options);
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new UsageException($"--{name} is required.");

    private static List<SemanticVersion> Available(Dictionary<string, string> options) =>
        [.. options.GetValueOrDefault("available", string.Empty)
            .Split([' ', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => SemanticVersion.Parse(v.TrimStart('v')))];

    private sealed class UsageException(string message) : Exception(message);
}
