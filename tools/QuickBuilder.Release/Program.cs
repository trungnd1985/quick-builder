namespace QuickBuilder.Release;

/// <summary>
/// <c>qb-release</c>: the release rules that must be right, in one tested place (019 research
/// RL-22, contracts/release-pipeline.md §5).
/// </summary>
/// <remarks>
/// Exit codes: 0 when the check passes, 1 when a rule fails (the message names the file,
/// section or version), 2 for a usage error.
/// </remarks>
internal static class Program
{
    internal const int Pass = 0;
    internal const int RuleFailed = 1;
    internal const int UsageError = 2;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error, Directory.GetCurrentDirectory());

    internal static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string repositoryRoot)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: qb-release <command> [options]");
            return UsageError;
        }

        error.WriteLine($"Unknown command '{args[0]}'.");
        return UsageError;
    }
}
