using System.Security.Cryptography;
using System.Text;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering.Forms;
using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.UnitTests.Rendering.Forms;

/// <summary>
/// The form script and the policy of pages with a form (018 FR-010; research FS-10, FS-11;
/// public-forms.md §2.2). The admin's <c>formScript.spec.ts</c> runs the same file.
/// </summary>
public sealed class FormScriptTests
{
    private static string Page(string blocks)
    {
        var harness = new RenderHarness { CurrentPageId = Guid.NewGuid(), Path = "/contact" };
        harness.Menus[Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40")] = new LiveMenu(
            1,
            System.Text.Json.JsonElement.Parse("""{ "schemaVersion": 1, "entries": [ { "id": "a", "label": "Home", "target": { "type": "url", "href": "https://example.com" }, "children": [] } ] }"""),
            "Main");
        return harness.Page(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ {{blocks}} ] }]"""));
    }

    private static string Form(string id, string extra = "") =>
        $$"""{ "id": "{{id}}", "type": "form", "props": { "name": "F", "fields": [ { "id": "a00000000001", "type": "email", "label": "Email" } ]{{extra}} }, "children": [] }""";

    [Fact]
    public void The_script_is_the_embedded_file_small_and_closed_by_nothing_inside_it()
    {
        var file = File.ReadAllText(RepositoryFiles.Path("src", "Modules", "Rendering", "QuickBuilder.Rendering", "Resources", "form-script.js"))
            .ReplaceLineEndings("\n").TrimEnd('\n');

        Assert.Equal(file, FormScript.Content);
        Assert.True(Encoding.UTF8.GetByteCount(FormScript.Content) <= 8192, $"{Encoding.UTF8.GetByteCount(FormScript.Content)} bytes");
        Assert.DoesNotContain("</script", FormScript.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<script>" + FormScript.Content + "</script>", FormScript.Element);
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(FormScript.Content))), FormScript.Hash);
    }

    [Fact]
    public void It_is_written_once_after_the_first_form_however_many_forms_there_are()
    {
        var html = Page($"{Form("one")}, {Form("two")}");

        Assert.Equal(1, html.Split(FormScript.Element).Length - 1);
        Assert.True(html.IndexOf(FormScript.Element, StringComparison.Ordinal) > html.IndexOf("id=\"qb-p.one\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf(FormScript.Element, StringComparison.Ordinal) < html.IndexOf("id=\"qb-p.two\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_page_with_a_form_may_post_and_fetch_to_the_site_and_run_exactly_its_scripts()
    {
        var page = PageResponder.Render(Inputs($"{Form("one")}, {NavBlock}"), RenderHarness.Renderer);
        var csp = PageResponder.ToResponse(page).Headers["Content-Security-Policy"];

        Assert.Equal(FormScript.Hash, page.FormScriptHash);
        Assert.Contains($"script-src 'sha256-{NavScript.Hash}' 'sha256-{FormScript.Hash}'; ", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self'; ", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'; ", csp, StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_redirect_target_is_named_in_form_action()
    {
        var page = PageResponder.Render(
            Inputs($$"""{{Form("one", ", \"redirect\": { \"type\": \"url\", \"href\": \"https://thanks.example:8443/done?x=1\" }")}}"""),
            RenderHarness.Renderer);

        Assert.Equal(["https://thanks.example:8443"], page.FormActionOrigins!);
        Assert.Contains("form-action 'self' https://thanks.example:8443; ", PageResponder.ToResponse(page).Headers["Content-Security-Policy"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_without_a_form_posts_nowhere_and_fetches_nothing()
    {
        var page = PageResponder.Render(Inputs("""{ "id": "t", "type": "text", "props": { "text": "Hi" }, "children": [] }"""), RenderHarness.Renderer);
        var csp = PageResponder.ToResponse(page).Headers["Content-Security-Policy"];

        Assert.Null(page.FormScriptHash);
        Assert.Contains("form-action 'none'; ", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("connect-src", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src", csp, StringComparison.Ordinal);
    }

    private const string NavBlock = """{ "id": "nav", "type": "navigation", "props": { "menuId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40" }, "children": [] }""";

    private static RenderInputs Inputs(string blocks)
    {
        var harness = new RenderHarness { CurrentPageId = Guid.NewGuid(), Path = "/contact" };
        harness.Menus[Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40")] = new LiveMenu(
            1,
            System.Text.Json.JsonElement.Parse("""{ "schemaVersion": 1, "entries": [ { "id": "a", "label": "Home", "target": { "type": "url", "href": "https://example.com" }, "children": [] } ] }"""),
            "Main");
        return harness.Inputs(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ {{blocks}} ] }]"""));
    }
}
