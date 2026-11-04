using System.Text.Json.Nodes;
using QuickBuilder.Editor.Features.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Editor;

/// <summary>Skip and rename keep a site's design and add only the tokens it lacks (020 research ST-10).</summary>
public sealed class DesignMergeTests
{
    [Fact]
    public void Tokens_the_target_lacks_are_added_and_its_own_values_win()
    {
        var target = JsonNode.Parse("""{"schemaVersion":1,"spacing":{"base":4},"radius":{"small":2}}""");
        var archive = JsonNode.Parse("""{"schemaVersion":1,"spacing":{"base":8,"large":32},"shadow":{"card":"0 1px 2px"}}""");

        var (merged, changed) = DesignArchivePart.AddMissing(target, archive);

        Assert.True(changed);
        Assert.Equal(4, merged!["spacing"]!["base"]!.GetValue<int>());
        Assert.Equal(32, merged["spacing"]!["large"]!.GetValue<int>());
        Assert.Equal("0 1px 2px", merged["shadow"]!["card"]!.GetValue<string>());
        Assert.Equal(2, merged["radius"]!["small"]!.GetValue<int>());
    }

    [Fact]
    public void Nothing_changes_when_the_target_holds_every_token_or_either_side_is_not_an_object()
    {
        Assert.False(DesignArchivePart.AddMissing(JsonNode.Parse("""{"a":{"b":1}}"""), JsonNode.Parse("""{"a":{"b":2}}""")).Changed);
        Assert.False(DesignArchivePart.AddMissing(JsonNode.Parse("""[1]"""), JsonNode.Parse("""{"a":1}""")).Changed);

        // Lists, such as colours, are kept whole: entries are not merged one by one.
        Assert.False(DesignArchivePart.AddMissing(JsonNode.Parse("""{"colors":[{"id":"a"}]}"""), JsonNode.Parse("""{"colors":[{"id":"b"}]}""")).Changed);
    }
}
