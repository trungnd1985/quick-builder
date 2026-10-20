using System.Security.Cryptography;
using System.Text;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.UnitTests.Rendering.Navigation;

/// <summary>
/// The one script a published page may carry, and the policy that admits exactly it (024
/// research NV-14, FR-020).
/// </summary>
public sealed class NavScriptTests
{
    [Fact]
    public void The_script_matches_its_golden_file()
    {
        var golden = RepositoryFiles.Fixtures("navigation", "nav-script.js");
        if (RepositoryFiles.UpdateGolden)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, NavScript.Content + "\n");
            return;
        }

        var content = NavScript.Content;
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n").TrimEnd('\n'), content);
    }

    [Fact]
    public void The_script_is_small()
    {
        Assert.True(Encoding.UTF8.GetByteCount(NavScript.Content) <= 2048, $"{Encoding.UTF8.GetByteCount(NavScript.Content)} bytes");
    }

    [Fact]
    public void Its_hash_is_the_hash_of_exactly_its_bytes()
    {
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(NavScript.Content))), NavScript.Hash);
        Assert.Equal("<script>" + NavScript.Content + "</script>", NavScript.Element);
        Assert.DoesNotContain("</script", NavScript.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_page_with_a_menu_admits_exactly_the_script_and_others_admit_none()
    {
        var withMenu = PageResponder.ToResponse(new RenderedPage([], "style", NavScript.Hash));
        var without = PageResponder.ToResponse(new RenderedPage([], "style"));

        Assert.Contains($"script-src 'sha256-{NavScript.Hash}'", withMenu.Headers["Content-Security-Policy"], StringComparison.Ordinal);
        Assert.DoesNotContain("script-src", without.Headers["Content-Security-Policy"], StringComparison.Ordinal);
        Assert.Equal(PageResponder.ContentSecurityPolicy("style"), without.Headers["Content-Security-Policy"]);
    }

    [Fact]
    public void A_rendered_page_records_the_script_only_when_it_shows_a_menu()
    {
        var (harness, document, title) = RendererGoldenTests.Prepare(Path.Combine(RepositoryFiles.ValidExamples, "navigation-minimal.json"));
        var withMenu = PageResponder.Render(harness.Inputs(document, title), RenderHarness.Renderer);
        var (plain, plainDocument, plainTitle) = RendererGoldenTests.Prepare(Path.Combine(RepositoryFiles.ValidExamples, "every-block.json"));
        var without = PageResponder.Render(plain.Inputs(plainDocument, plainTitle), RenderHarness.Renderer);

        Assert.Equal(NavScript.Hash, withMenu.ScriptHash);
        Assert.Null(without.ScriptHash);
    }
}
