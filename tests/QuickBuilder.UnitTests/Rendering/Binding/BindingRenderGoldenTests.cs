using System.Text;
using QuickBuilder.Rendering.Features.RenderPage;

namespace QuickBuilder.UnitTests.Rendering.Binding;

/// <summary>
/// Every resolve case of the binding contract renders to its checked-in HTML, byte for byte
/// (016 SC-003, research TB-09; tasks T041).
/// </summary>
/// <remarks>
/// The resolve cases say what each bound property resolves to; these say what the published
/// page then shows: bound values encoded in place, fallbacks, hidden blocks left out, rich
/// text through the allow-list, item and file links, refused addresses as plain text. Set
/// <c>QB_UPDATE_GOLDEN=1</c> to rewrite them, then review every changed file.
/// </remarks>
public sealed class BindingRenderGoldenTests
{
    public static TheoryData<string> Cases() => [.. ResolveCase.Names()];

    private static string Render(ResolveCase @case) =>
        Encoding.UTF8.GetString(PageResponder.Render(@case.Inputs(), RenderHarness.Renderer).Html);

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_page_with_bindings_renders_to_its_golden_file(string name)
    {
        var html = Render(ResolveCase.Load(name));
        var golden = RepositoryFiles.Fixtures("bindings", $"{name}.html");

        if (RepositoryFiles.UpdateGolden)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, html);
            return;
        }

        Assert.True(File.Exists(golden), $"No golden file for {name}. Run the tests with QB_UPDATE_GOLDEN=1, then review it.");
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), html);
    }

    [Fact]
    public void Bound_values_are_encoded_and_a_hidden_block_is_left_out()
    {
        var html = Render(ResolveCase.Load("broken-and-empty"));

        // blank-hidden and gone-hidden hide; their own text never appears for them.
        Assert.DoesNotContain("data-qb=\"p.blank-hidden\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-qb=\"p.gone-hidden\"", html, StringComparison.Ordinal);
        Assert.Contains("Hello world by ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsafe_address_in_a_field_never_becomes_a_link()
    {
        var html = Render(ResolveCase.Load("rich-image-link"));

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/site/blog/hello-world\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/a?b=1\"", html, StringComparison.Ordinal);
    }
}
