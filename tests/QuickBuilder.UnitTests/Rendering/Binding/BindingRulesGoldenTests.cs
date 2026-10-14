using System.Text.Json;
using QuickBuilder.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Binding;

/// <summary>
/// Every resolve case of the binding contract gives exactly its expected outcome on the server
/// (016 research TB-05, TB-09; binding-expression.md §4, §8; tasks T018).
/// </summary>
/// <remarks>
/// The admin's <c>bindingRules.spec.ts</c> runs the same cases through the editor's resolver, so
/// the canvas and the published page agree on every bound value and every broken binding.
/// </remarks>
public sealed class BindingRulesGoldenTests
{
    public static TheoryData<string, string, string> Outcomes()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var name in ResolveCase.Names())
        {
            foreach (var (node, prop, _) in ResolveCase.Load(name).Expected())
            {
                data.Add(name, node, prop);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void A_bound_property_resolves_to_its_expected_outcome(string caseName, string nodeId, string prop)
    {
        var @case = ResolveCase.Load(caseName);
        var expected = @case.Expected().Single(e => e.NodeId == nodeId && e.Prop == prop).Outcome;
        var context = @case.Context();

        var bound = context.Bound(@case.Node(nodeId), prop);

        var actual = Describe(bound);
        Assert.True(JsonElement.DeepEquals(expected, actual), $"{caseName} {nodeId}.{prop}: expected {expected.GetRawText()}, got {actual.GetRawText()}");
    }

    [Fact]
    public void Every_case_has_outcomes()
    {
        Assert.NotEmpty(ResolveCase.Names());
        Assert.All(ResolveCase.Names(), name => Assert.NotEmpty(ResolveCase.Load(name).Expected()));
    }

    [Fact]
    public void An_unbound_property_is_unbound()
    {
        var @case = ResolveCase.Load("text-every-type");
        var context = @case.Context();

        Assert.Equal(BoundState.Unbound, context.Bound(@case.Node("title"), "level").State);
    }

    /// <summary>A bound value in the shape the case files write outcomes in.</summary>
    private static JsonElement Describe(BoundValue bound)
    {
        var outcome = bound.State switch
        {
            BoundState.Value when bound.Text is { } text => new Dictionary<string, object?> { ["text"] = text },
            BoundState.Value when bound.RichText is { } richText => new Dictionary<string, object?> { ["richText"] = richText },
            BoundState.Value when bound.MediaId is { } media => new Dictionary<string, object?> { ["media"] = media.ToString() },
            BoundState.Value when bound.Link is { } link => new Dictionary<string, object?> { ["link"] = link.Href },
            BoundState.Value when bound.Flag => new Dictionary<string, object?> { ["flag"] = true },
            BoundState.Hide => new Dictionary<string, object?> { ["hide"] = true },
            _ => new Dictionary<string, object?> { ["fallback"] = true },
        };

        if (bound.Broken is { } broken)
        {
            outcome["broken"] = broken;
        }

        return JsonSerializer.SerializeToElement(outcome);
    }
}
