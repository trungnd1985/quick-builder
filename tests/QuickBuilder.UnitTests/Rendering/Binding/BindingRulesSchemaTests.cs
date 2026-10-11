using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Binding;

/// <summary>
/// The renderer's bindable properties are exactly the <c>bind</c> keys the page schema allows
/// (016 research TB-05, Principle VI; tasks T048). The admin's <c>bindableProps.spec.ts</c>
/// checks its own table the same way.
/// </summary>
public sealed class BindingRulesSchemaTests
{
    public static TheoryData<string, string[]> BindKeys()
    {
        using var stream = PageSchema.OpenSchema(PageSchema.CurrentVersion);
        using var schema = JsonDocument.Parse(stream);
        var data = new TheoryData<string, string[]>();

        foreach (var definition in schema.RootElement.GetProperty("$defs").EnumerateObject())
        {
            if (!definition.Value.TryGetProperty("properties", out var fields)
                || !fields.TryGetProperty("type", out var type) || !type.TryGetProperty("const", out var name)
                || !fields.TryGetProperty("props", out var props) || !props.TryGetProperty("properties", out var list))
            {
                continue;
            }

            var keys = list.TryGetProperty("bind", out var bind) && bind.TryGetProperty("properties", out var bindable)
                ? bindable.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray()
                : [];
            data.Add(name.GetString()!, keys);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BindKeys))]
    public void A_block_binds_exactly_the_properties_its_schema_allows(string type, string[] schemaKeys)
    {
        string[] candidates = ["text", "label", "alt", "media", "link", "content", "level", "url", "title", "source"];
        var bindable = candidates.Union(schemaKeys).Where(prop => BindingRules.KindOf(type, prop) is not null).Order(StringComparer.Ordinal);

        Assert.Equal(schemaKeys, bindable);
    }
}
