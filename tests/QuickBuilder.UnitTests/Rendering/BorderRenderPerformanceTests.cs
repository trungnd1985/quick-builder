using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// Borders cost almost nothing to render (066 SC-007, tasks T047): the 200-block page the render
/// budget is measured on (012 SC-003), with a border, corners and inner spacing on every third
/// block, renders within the same budget and close to the page without them.
/// </summary>
/// <remarks>
/// A wall-clock budget, so CI runs it in its report-only step with the other budgets (PR #31's
/// decision); it is asserted on a development machine and in the quickstart run. The fixture
/// itself is left alone: it is the page the 012 budget is measured on.
/// </remarks>
[Trait("Category", "Performance")]
public sealed class BorderRenderPerformanceTests(ITestOutputHelper output)
{
    private const int Runs = 100;

    /// <summary>Per whole-page render, in milliseconds: the same loose guard the rich text check uses.</summary>
    private const double Budget = 20;

    /// <summary>How much slower the bordered page may be than the plain one, as a ratio.</summary>
    private const double Overhead = 1.5;

    [Fact]
    public void The_two_hundred_block_page_with_borders_renders_within_the_budget()
    {
        var (harness, plain, title) = RendererGoldenTests.Prepare(RepositoryFiles.Fixtures("page-200-blocks.json"));
        var bordered = WithBorders(plain, out var count);
        Assert.True(count >= 60, $"Only {count} blocks were given a border.");
        Assert.Contains("qb-border--thin", harness.Page(bordered, title), StringComparison.Ordinal);

        var plainEach = Time(() => harness.Page(plain, title));
        var borderedEach = Time(() => harness.Page(bordered, title));

        output.WriteLine($"page-200-blocks: {plainEach:0.00} ms plain, {borderedEach:0.00} ms with {count} borders");
        Assert.True(borderedEach < Budget, $"A render took {borderedEach:0.00} ms on average; the budget is {Budget} ms.");
        Assert.True(borderedEach < plainEach * Overhead + 1, $"Borders made a render {borderedEach / plainEach:0.00} times slower.");
    }

    private static double Time(Action render)
    {
        render();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            render();
        }

        return watch.Elapsed.TotalMilliseconds / Runs;
    }

    /// <summary>The document with a full border on every third block that may have one.</summary>
    private static JsonElement WithBorders(JsonElement document, out int count)
    {
        var root = JsonNode.Parse(document.GetRawText())!;
        var seen = 0;
        var given = 0;

        void Walk(JsonNode node)
        {
            var type = (string?)node["type"];
            if (type is not ("root" or "list-empty") && seen++ % 3 == 0)
            {
                var box = type is "section" or "container";
                var border = new JsonObject
                {
                    ["width"] = "thin",
                    ["style"] = "dashed",
                    ["color"] = new JsonObject { ["token"] = "color-brand" },
                    ["radius"] = new JsonObject { ["token"] = "radius-md" },
                };
                if (!box)
                {
                    border["padding"] = new JsonObject { ["token"] = "space-sm" };
                }

                node["props"]!["border"] = border;
                given++;
            }

            foreach (var child in node["children"]!.AsArray())
            {
                Walk(child!);
            }
        }

        Walk(root["root"]!);
        count = given;
        return JsonSerializer.SerializeToElement(root);
    }
}
