using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Rendering.Seo.StructuredData;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>No value can end the JSON-LD element, and every value survives (017 FR-036; research SE-18).</summary>
public sealed class JsonLdWriterTests
{
    [Theory]
    [InlineData("</script><script>alert(1)</script>")]
    [InlineData("<!-- comment")]
    [InlineData("a & b ' c + d")]
    [InlineData("line\u2028separator\u2029paragraph")]
    [InlineData("café — naïve")]
    public void A_hostile_value_never_appears_raw_and_parses_back_unchanged(string value)
    {
        var json = JsonLdWriter.Write([new JsonObject { ["@type"] = "Thing", ["name"] = value }])!;

        Assert.DoesNotContain("<", json, StringComparison.Ordinal);
        Assert.DoesNotContain(">", json, StringComparison.Ordinal);
        Assert.DoesNotContain("&", json, StringComparison.Ordinal);
        Assert.DoesNotContain("'", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2028", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2029", json, StringComparison.Ordinal);

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(value, parsed.RootElement.GetProperty("@graph")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Nodes_are_one_graph_in_schema_org_context()
    {
        var json = JsonLdWriter.Write([new JsonObject { ["@type"] = "A" }, new JsonObject { ["@type"] = "B" }]);

        Assert.Equal("""{"@context":"https://schema.org","@graph":[{"@type":"A"},{"@type":"B"}]}""", json);
    }

    [Fact]
    public void Nothing_to_describe_writes_nothing()
    {
        Assert.Null(JsonLdWriter.Write([]));
    }
}
