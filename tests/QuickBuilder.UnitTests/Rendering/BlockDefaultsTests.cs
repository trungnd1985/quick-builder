using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>Defaults come from the page schema, the one block definition (012 research PR-09).</summary>
public sealed class BlockDefaultsTests
{
    private static readonly BlockDefaults Defaults = BlockDefaults.Current;

    [Theory]
    [InlineData("section", "width", "\"contained\"")]
    [InlineData("section", "paddingY", "{\"token\":\"space-xl\"}")]
    [InlineData("container", "direction", "\"vertical\"")]
    [InlineData("container", "stackOnMobile", "true")]
    [InlineData("heading", "level", "2")]
    [InlineData("heading", "color", "{\"token\":\"color-text\"}")]
    [InlineData("text", "size", "{\"token\":\"text-md\"}")]
    [InlineData("button", "style", "\"primary\"")]
    [InlineData("spacer", "height", "{\"token\":\"space-lg\"}")]
    [InlineData("divider", "thickness", "\"thin\"")]
    [InlineData("embed", "aspectRatio", "\"16:9\"")]
    public void A_property_default_is_read_from_the_schema(string type, string property, string expected)
    {
        Assert.Equal(expected, System.Text.Json.JsonSerializer.Serialize(Defaults.Of(type, property)!.Value));
    }

    [Theory]
    [InlineData("heading")]
    [InlineData("text")]
    [InlineData("button")]
    [InlineData("container")]
    public void A_default_on_a_referenced_definition_is_followed(string type)
    {
        Assert.Equal("start", Defaults.Of(type, "align")!.Value.GetString());
    }

    [Fact]
    public void A_property_without_a_default_has_none()
    {
        Assert.Null(Defaults.Of("heading", "text"));
        Assert.Null(Defaults.Of("section", "background"));
        Assert.Null(Defaults.Of("no-such-type", "anything"));
    }

    [Fact]
    public void Every_node_type_of_the_schema_is_known()
    {
        Assert.Equal(
            ["button", "collection-list", "component-ref", "container", "divider", "embed", "heading", "image", "list-card", "list-empty", "rich-text", "root", "section", "spacer", "text"],
            Defaults.Types.Order(StringComparer.Ordinal));
    }
}
