using System.Diagnostics;
using Xunit.Abstractions;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// A rich text at every limit at once — 50 images, 20 tables, one of 200 rows × 20 columns,
/// 20,000 characters — renders well within the page render budget (035 SC-006, research RT-15).
/// </summary>
/// <remarks>
/// A wall-clock budget, so CI runs it in a report-only step, as it does every other budget:
/// shared runners have measured it at 26 ms and, on a degraded one, 257 ms with nothing
/// changed (PR #31's decision). It is asserted on a development machine and in the quickstart run.
/// </remarks>
[Trait("Category", "Performance")]
public sealed class RichTextRenderPerformanceTests(ITestOutputHelper output)
{
    private const int Runs = 100;

    /// <summary>
    /// Per whole-page render, in milliseconds. It takes about 4 ms on a development machine
    /// (research RT-15's target is 5 ms); the guard is looser, so a busy CI machine does not fail
    /// it, while a regression of several times still does.
    /// </summary>
    private const double Budget = 20;

    [Fact]
    public void The_limit_document_renders_within_the_budget()
    {
        var (harness, document, title) = RendererGoldenTests.Prepare(Path.Combine(RepositoryFiles.ValidExamples, "rich-text-limits.json"));
        harness.Page(document, title);

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            harness.Page(document, title);
        }

        var each = watch.Elapsed.TotalMilliseconds / Runs;
        output.WriteLine($"rich-text-limits: {each:0.00} ms per render");
        Assert.True(each < Budget, $"A render took {each:0.00} ms on average; the budget is {Budget} ms.");
    }
}
