using System.Reflection;
using NetArchTest.Rules;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Drafts never reach the public site, and previews never reach the render cache (022 research
/// PV-21; spec FR-022, FR-023; constitution Principle VII).
/// </summary>
/// <remarks>
/// The guarantees are structural: the public path cannot hold a type that reads drafts, the
/// published readers cannot use the preview read contexts, and preview code cannot build a
/// render cache key. A change that breaks one fails here before it can leak anything.
/// </remarks>
public sealed class PreviewBoundaryTests
{
    private static readonly Assembly Rendering = typeof(QuickBuilder.Rendering.RenderingModule).Assembly;
    private static readonly Assembly Pages = typeof(QuickBuilder.Pages.PagesModule).Assembly;
    private static readonly Assembly Cms = typeof(QuickBuilder.Cms.CmsModule).Assembly;

    /// <summary>The types that serve the published site (012, 016, 017).</summary>
    private static readonly string[] PublicPath =
    [
        "QuickBuilder.Rendering.Features.RenderPage.RenderPageController",
        "QuickBuilder.Rendering.Features.RenderPage.RenderPageHandler",
        "QuickBuilder.Rendering.Features.RenderPage.PageResponder",
        "QuickBuilder.Rendering.Rendering.LiveContentSource",
        "QuickBuilder.Rendering.Rendering.PageComposer",
        "QuickBuilder.Rendering.Features.Sitemap.SitemapController",
        "QuickBuilder.Rendering.Features.Sitemap.SitemapBuilder",
        "QuickBuilder.Rendering.Features.Robots.RobotsController",
        "QuickBuilder.Rendering.Features.NotFound.NotFoundPage",
    ];

    /// <summary>What reads drafts.</summary>
    private static readonly string[] DraftReaders =
    [
        "QuickBuilder.Pages.Contracts.Publishing.IPreviewContentReader",
        "QuickBuilder.Cms.Contracts.Publishing.IPreviewItemReader",
        "QuickBuilder.Editor.Contracts.IDesignTokenReader",
        "QuickBuilder.Rendering.Features.Preview.PreviewContentSource",
    ];

    [Fact]
    public void The_public_path_cannot_reach_a_draft()
    {
        var offenders = new List<string>();
        foreach (var fullName in PublicPath)
        {
            Assert.True(Rendering.GetType(fullName) is not null, $"{fullName} no longer exists: update the list.");

            var name = fullName[(fullName.LastIndexOf('.') + 1)..];
            var result = Types.InAssembly(Rendering)
                .That().HaveName(name).And().ResideInNamespace(fullName[..fullName.LastIndexOf('.')])
                .ShouldNot().HaveDependencyOnAny(DraftReaders)
                .GetResult();
            offenders.AddRange(result.FailingTypeNames ?? []);
        }

        Assert.True(offenders.Count == 0, "Public types depend on a draft reader: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Preview_code_never_builds_a_render_cache_key()
    {
        var offenders = Types.InAssembly(Rendering)
            .That().ResideInNamespace("QuickBuilder.Rendering.Features.Preview")
            .ShouldNot().HaveDependencyOnAny(
                "QuickBuilder.Rendering.Features.RenderPage.RenderKey",
                "QuickBuilder.Rendering.Features.RenderPage.ItemRenderKey")
            .GetResult();

        Assert.True(offenders.IsSuccessful, "Preview types build render keys: " + string.Join(", ", offenders.FailingTypeNames ?? []));
    }

    [Fact]
    public void Every_preview_controller_answers_through_the_preview_responses()
    {
        var controllers = Types.InAssembly(Rendering)
            .That().ResideInNamespace("QuickBuilder.Rendering.Features.Preview")
            .And().Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .GetTypes()
            .ToList();

        Assert.NotEmpty(controllers);
        var result = Types.InAssembly(Rendering)
            .That().ResideInNamespace("QuickBuilder.Rendering.Features.Preview")
            .And().Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .Should().HaveDependencyOnAny(
                "QuickBuilder.Rendering.Features.Preview.PreviewResponse",
                "QuickBuilder.Rendering.Features.Preview.NotAvailablePage",
                "QuickBuilder.Rendering.Features.Preview.PreviewMedia")
            .GetResult();

        Assert.True(result.IsSuccessful, "Preview controllers answer another way: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("pages")]
    [InlineData("cms")]
    public void The_published_readers_never_use_the_preview_read_contexts(string module)
    {
        var (assembly, context, readers) = module == "pages"
            ? (Pages, "QuickBuilder.Pages.Infrastructure.PreviewReadDbContext",
                new[] { "PublishedContentReader", "SitemapReader", "RedirectReader", "TemplateReader" })
            : (Cms, "QuickBuilder.Cms.Infrastructure.PreviewReadDbContext", new[] { "PublishedItemReader" });

        var result = Types.InAssembly(assembly)
            .That().HaveName(readers[0])
            .Or().HaveNameMatching("^(" + string.Join('|', readers) + ")$")
            .ShouldNot().HaveDependencyOn(context)
            .GetResult();

        Assert.True(result.IsSuccessful, "Published readers use the preview context: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
