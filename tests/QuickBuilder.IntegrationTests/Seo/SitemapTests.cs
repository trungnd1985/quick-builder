using System.Net;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 3: each site's sitemap lists every indexable address once, from published data
/// only (017 FR-022 to FR-024, FR-027, SC-006, SC-007; research SE-14; public-seo.md §5).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SitemapTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly XNamespace Sm = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private TemplatesWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-sitemap");

        await _world.PublishedPostAsync("shown", new JsonObject { ["f1"] = "Shown" });
        await _world.PublishedPostAsync("hidden-post", new JsonObject { ["f1"] = "Hidden", ["f4"] = true });

        var blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog"));
        var template = await _world.PageAsync("post", blog);
        await _world.TemplateOkAsync(template, _world.Posts);
        await _world.PublishDocumentAsync(template, SeoApi.DocumentWithSeo("Post", """{ "bind": { "noindex": { "value": { "field": "f4" } } } }"""));

        foreach (var (slug, seo) in new[]
        {
            ("about", (string?)null),
            ("hidden", """{ "noindex": true }"""),
            ("left-out", """{ "noSitemap": true }"""),
            ("copy", """{ "canonical": { "type": "url", "href": "https://elsewhere.test/original" } }"""),
        })
        {
            var page = await _world.PageAsync(slug);
            await _world.PublishDocumentAsync(page, seo is null ? PublishingApi.HeadingDocument(slug) : SeoApi.DocumentWithSeo(slug, seo));
        }

        await _world.PageAsync("draft-only");
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private async Task<(HttpResponseMessage Response, XDocument Xml)> FetchAsync(string address = "/sitemap.xml")
    {
        var response = await _world.Visitor().GetAsync(new Uri(address, UriKind.Relative)).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (response, response.StatusCode == HttpStatusCode.OK ? XDocument.Parse(body) : new XDocument());
    }

    private static string[] Locs(XDocument xml) => [.. xml.Descendants(Sm + "loc").Select(l => l.Value)];

    [Fact]
    public async Task It_lists_every_indexable_page_and_item_once_and_nothing_else()
    {
        var (response, xml) = await FetchAsync().ConfigureAwait(true);

        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var host = $"https://{QuickBuilder.IntegrationTests.Cms.CmsScenario.HostA}";
        Assert.Equal(
            [$"{host}/about", $"{host}/blog", $"{host}/blog/shown"],
            Locs(xml).Order(StringComparer.Ordinal));
        Validate(xml, "sitemap-0.9.xsd");
    }

    [Fact]
    public async Task An_item_page_s_last_modified_is_the_later_of_the_item_and_the_template()
    {
        var (_, xml) = await FetchAsync().ConfigureAwait(true);
        var item = xml.Descendants(Sm + "url").Single(u => u.Element(Sm + "loc")!.Value.EndsWith("/blog/shown", StringComparison.Ordinal));
        var page = xml.Descendants(Sm + "url").Single(u => u.Element(Sm + "loc")!.Value.EndsWith("/blog", StringComparison.Ordinal));

        Assert.Matches("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$", item.Element(Sm + "lastmod")!.Value);
        Assert.True(string.CompareOrdinal(item.Element(Sm + "lastmod")!.Value, page.Element(Sm + "lastmod")!.Value) >= 0);
    }

    [Fact]
    public async Task An_unchanged_sitemap_answers_not_modified()
    {
        var (first, _) = await FetchAsync().ConfigureAwait(true);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/sitemap.xml", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag!.Tag);

        var second = await _world.Visitor().SendAsync(request).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task A_numbered_file_of_a_single_file_sitemap_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await FetchAsync("/sitemap-1.xml").ConfigureAwait(true)).Response.StatusCode);
    }

    [Fact]
    public async Task Another_site_s_sitemap_lists_only_its_own_addresses()
    {
        var other = await _world.Visitor(QuickBuilder.IntegrationTests.Cms.CmsScenario.HostB).GetStringAsync(new Uri("/sitemap.xml", UriKind.Relative)).ConfigureAwait(true);

        Assert.Empty(Locs(XDocument.Parse(other)));
    }

    [Fact]
    public async Task A_discouraged_site_has_an_empty_sitemap_marked_not_to_index()
    {
        await SeoApi.SetSearchVisibilityAsync(_world.Admin, _world.Site, discourage: true).ConfigureAwait(true);

        var (response, xml) = await FetchAsync().ConfigureAwait(true);

        Assert.Empty(Locs(xml));
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task More_than_fifty_thousand_addresses_make_an_index_of_numbered_files()
    {
        await _world.Cms.WithDatabaseAsync(db => db.Database.ExecuteSqlRawAsync(SeedPublishedItems, _world.Site, _world.Posts)).ConfigureAwait(true);

        var (index, xml) = await FetchAsync().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("sitemapindex", xml.Root!.Name.LocalName);
        Validate(xml, "siteindex-0.9.xsd");
        var files = Locs(xml);
        Assert.Equal(2, files.Length);

        var total = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var (part, partXml) = await FetchAsync(new Uri(file).AbsolutePath).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, part.StatusCode);
            var locs = Locs(partXml);
            Assert.True(locs.Length <= 50_000);
            total += locs.Length;
            seen.UnionWith(locs);
        }

        Assert.Equal(seen.Count, total);
        Assert.Equal(HttpStatusCode.NotFound, (await FetchAsync("/sitemap-3.xml").ConfigureAwait(true)).Response.StatusCode);
    }

    /// <summary>
    /// 50,001 published items in one statement, with their slug rows, as the smoke tests seed
    /// (their helper is quoted for a shell, so it cannot run here).
    /// </summary>
    private const string SeedPublishedItems =
        """
        WITH seeded AS (
            INSERT INTO cms.items (id, site_id, collection_id, draft, published, created_at_utc, updated_at_utc, slug, published_slug, status, revision,
                name, search_text, attention, index_version, created_by, updated_by, published_at_utc, first_published_at_utc, published_by, live_version, next_version_number)
            SELECT gen_random_uuid(), {0}, {1}, v.doc, v.doc, now(), now(), 'bulk-' || n, 'bulk-' || n, 'published', 1,
                'Item ' || n, 'item ' || n, NULL, 2, '00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000000',
                now(), now(), '00000000-0000-0000-0000-000000000000', 1, 2
            FROM generate_series(1, 50001) AS n
            CROSS JOIN LATERAL (SELECT jsonb_build_object('schemaVersion', 1, 'values', jsonb_build_object('f1', 'Item ' || n)) AS doc) v
            RETURNING id, site_id, collection_id, slug)
        INSERT INTO cms.item_slugs (collection_id, slug, item_id, site_id)
        SELECT collection_id, slug, id, site_id FROM seeded
        """;

    private static void Validate(XDocument xml, string schemaFile)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, Path.Combine(FixturesDirectory(), schemaFile));
        var errors = new List<string>();
        xml.Validate(schemas, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    private static string FixturesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "tests", "fixtures");
    }
}
