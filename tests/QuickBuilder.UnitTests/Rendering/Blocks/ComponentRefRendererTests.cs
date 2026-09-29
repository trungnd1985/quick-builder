using System.Text.Json;
using QuickBuilder.Pages.Contracts.Publishing;
using static QuickBuilder.UnitTests.Rendering.RenderHarness;

namespace QuickBuilder.UnitTests.Rendering.Blocks;

/// <summary>Shared components as published: drawn in place from their live version (012 FR-002, research PR-04).</summary>
public sealed class ComponentRefRendererTests
{
    private readonly RenderHarness _harness = new();

    [Fact]
    public void A_component_is_drawn_in_place_with_its_own_style_scope()
    {
        var component = Guid.NewGuid();
        using var document = JsonDocument.Parse("""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[]}]}}
            """);
        _harness.Components[component] = new LiveDocument(1, document.RootElement.Clone());

        var page = _harness.Page(Document($$$"""
            [{"id":"ref","type":"component-ref","props":{"componentId":"{{{component}}}"},"children":[]},
             {"id":"s","type":"section","props":{},"children":[]}]
            """));

        Assert.Contains($"data-qb=\"c.{component:N}.s\"", page, StringComparison.Ordinal);
        Assert.Contains("data-qb=\"p.s\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-qb=\"p.ref\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_without_a_live_version_draws_nothing()
    {
        var page = _harness.Page(Document($$$"""[{"id":"ref","type":"component-ref","props":{"componentId":"{{{Guid.NewGuid()}}}"},"children":[]}]"""));

        Assert.Contains("<div class=\"qb-canvas qb-page\"></div>", page, StringComparison.Ordinal);
    }
}
