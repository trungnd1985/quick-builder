using System.Text.RegularExpressions;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Forms never logs what a visitor sent or who is told about it (018 FR-022; research FS-17):
/// no log message template in the module names a value, a recipient, the search text or an
/// address. The log-capture assertions of <c>SubmitEndpointTests</c> and <c>NotificationTests</c>
/// check what actually reaches the logs; this rule stops a template that would.
/// </summary>
public sealed partial class FormsLoggingTests
{
    // Placeholder names that would carry personal data, matched case-insensitively.
    private static readonly string[] Forbidden =
        ["value", "values", "recipient", "recipients", "email", "address", "searchtext", "search", "subject", "body", "reply", "serverreply", "to", "from", "name"];

    [Fact]
    public void No_log_template_in_the_forms_module_names_personal_data()
    {
        var module = Path.Combine(RepositoryRoot(), "src", "Modules", "Forms", "QuickBuilder.Forms");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(module, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(file);
            foreach (Match call in LogCall().Matches(source))
            {
                foreach (Match placeholder in Placeholder().Matches(call.Groups["template"].Value))
                {
                    var name = placeholder.Groups["name"].Value;
                    if (Forbidden.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetFileName(file)}: {{{name}}} in \"{call.Groups["template"].Value}\"");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Forms log templates must not name personal data:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_rule_sees_log_templates()
    {
        // Guards the rule itself: a scan that matched nothing would pass for the wrong reason.
        var sample = """logger.LogWarning(ex, "Sent to {Recipient} for {FormId}.", a, b);""";

        var templates = LogCall().Matches(sample).Select(m => m.Groups["template"].Value).ToList();

        Assert.Equal(["Sent to {Recipient} for {FormId}."], templates);
        Assert.Equal(["Recipient", "FormId"], Placeholder().Matches(templates[0]).Select(m => m.Groups["name"].Value));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    // logger.LogXxx( [exception,] "template" — the first string literal of the call.
    [GeneratedRegex(@"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*,\s*)?""(?<template>(?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex LogCall();

    [GeneratedRegex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
