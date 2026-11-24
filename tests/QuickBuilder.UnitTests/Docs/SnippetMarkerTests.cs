using QuickBuilder.Docs.Snippets;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// Runnable documentation: markers, their rules, and the script and files written from them
/// (026 T008; data-model §6; contracts/runnable-snippets.md §1–2).
/// </summary>
public sealed class SnippetMarkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qb-docs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (IReadOnlyList<Snippet> Snippets, List<string> Problems) Parse(string text)
    {
        var problems = new List<string>();
        var snippets = MarkerParser.Parse("page.md", text, problems);
        MarkerParser.CheckProcedures(snippets, problems);
        return (snippets, problems);
    }

    [Fact]
    public void Run_and_file_markers_take_the_next_fenced_block()
    {
        var (snippets, problems) = Parse("""
            Intro.

            <!-- qb:file procedure=nginx step=1 path=nginx/site.conf substitute=test-certificates -->
            ```nginx
            server { }
            ```

            <!-- qb:run procedure=nginx step=2 -->
            ```bash
            docker compose up -d
            ```
            """);

        Assert.Empty(problems);
        Assert.Collection(
            snippets,
            file =>
            {
                Assert.Equal(("file", 1, "nginx/site.conf", "test-certificates"), (file.Kind, file.Step, file.FilePath, string.Join(",", file.Substitutes)));
                Assert.Equal("server { }", file.Body);
            },
            run => Assert.Equal(("run", 2, "docker compose up -d"), (run.Kind, run.Step, run.Body)));
    }

    [Theory]
    [InlineData("<!-- qb:run procedure=install step=1 -->\nNot a block.", "is not followed by a fenced block")]
    [InlineData("<!-- qb:run procedure=nope step=1 -->\n```bash\nx\n```", "unknown procedure 'nope'")]
    [InlineData("<!-- qb:run procedure=install step=one -->\n```bash\nx\n```", "step must be a whole number")]
    [InlineData("<!-- qb:run procedure=install step=1 substitute=magic -->\n```bash\nx\n```", "unknown substitution 'magic'")]
    [InlineData("<!-- qb:file procedure=install step=1 -->\n```text\nx\n```", "qb:file needs path=")]
    [InlineData("<!-- qb:file procedure=install step=1 path=../x -->\n```text\nx\n```", "must be relative and stay inside")]
    [InlineData("<!-- qb:run procedure=install step=1 -->\n```powershell\nx\n```", "must be bash or sh")]
    [InlineData("<!-- qb:file procedure=install step=1 path=a.txt -->\n```text\nx\n```", "no qb:run step")]
    [InlineData("<!-- qb:run procedure=install step=1 -->\n```bash\nx\n```\n<!-- qb:run procedure=install step=1 -->\n```bash\ny\n```", "step 1 is used 2 times")]
    public void Marker_mistakes_are_reported(string text, string expected)
    {
        var (_, problems) = Parse(text);

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Write_produces_a_strict_script_files_verbatim_and_applies_substitutions()
    {
        var docs = Path.Combine(_root, "docs", "operators");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(_root, "QuickBuilder.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(docs, "install.md"), """
            <!-- qb:run procedure=install step=2 substitute=secret -->
            ```bash
            echo "POSTGRES_PASSWORD=change-me-before-real-use" > .env
            ```

            <!-- qb:run procedure=install step=1 substitute=release-compose -->
            ```bash
            curl -fsSLO https://example.test/docker-compose.yml
            ```

            <!-- qb:file procedure=install step=3 path=conf/a.txt -->
            ```text
            exact text
            ```
            """);
        var output = Path.Combine(_root, "out");

        var exit = SnippetCommands.Write(
            _root,
            "install",
            output,
            new Dictionary<string, string> { ["secret"] = "s3cr3t", ["release-compose"] = "/tmp/compose.yml" },
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exit);
        var script = File.ReadAllText(Path.Combine(output, "run.sh"));
        Assert.StartsWith("#!/usr/bin/env bash", script, StringComparison.Ordinal);
        Assert.Contains("set -euo pipefail", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("cp '/tmp/compose.yml'", StringComparison.Ordinal) < script.IndexOf("POSTGRES_PASSWORD=s3cr3t", StringComparison.Ordinal));
        Assert.DoesNotContain("change-me", script, StringComparison.Ordinal);
        Assert.Equal("exact text\n", File.ReadAllText(Path.Combine(output, "conf", "a.txt")));
    }

    [Fact]
    public void Write_fails_when_a_needed_substitution_is_missing()
    {
        var docs = Path.Combine(_root, "docs", "operators");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "p.md"), "<!-- qb:run procedure=install step=1 substitute=secret -->\n```bash\necho change-me-before-real-use\n```\n");
        var error = new StringWriter();

        var exit = SnippetCommands.Write(_root, "install", Path.Combine(_root, "out"), new Dictionary<string, string>(), TextWriter.Null, error);

        Assert.Equal(1, exit);
        Assert.Contains("needs --substitute secret=<value>", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Pair_substitutions_must_find_what_they_replace()
    {
        var problems = new List<string>();

        Assert.Equal("tls /certs/a.pem", Substitutions.Apply("test-certificates", "tls you@example.com>tls /certs/a.pem", "tls you@example.com", "p.md:1", problems));
        Assert.Null(Substitutions.Apply("test-certificates", "absent>x", "tls you@example.com", "p.md:1", problems));
        Assert.Contains(problems, p => p.Contains("the page changed", StringComparison.Ordinal));
    }
}
