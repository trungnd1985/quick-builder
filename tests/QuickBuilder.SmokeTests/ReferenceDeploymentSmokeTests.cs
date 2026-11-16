using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// Boots the built image with the reference compose file and walks the operator journey
/// (Constitution Principle X, plan exception E-3).
/// </summary>
/// <remarks>
/// Everything else in the suite tests the application. This tests the <em>deployment</em> —
/// the Dockerfile, the compose file, the loopback binding, the health probes and the
/// startup ordering. Those are what an operator actually meets, and none of them is
/// exercised by <c>WebApplicationFactory</c>, which bypasses the image entirely.
/// <para>
/// The constitution specifies this smoke test as setup wizard → create page → publish →
/// view. Feature 002 added the first step: the journey now begins with the setup wizard,
/// reading the setup code from the container logs as an operator would, and runs every
/// later step as the administrator it created. Feature 010 added "create page": a page, a
/// child page and a saved draft. 012 added publish and view: a component and a page are
/// published and read back from the site's host name, which closes plan exception E-3. 016
/// added a template: a published item answers at its own address, its title bound in.
/// Driving the editor in a browser still waits for #25 (exception E-5, unchanged).
/// </para>
/// <para>
/// Excluded from the normal test run — CI filters on <c>FullyQualifiedName!~SmokeTests</c>
/// — because it builds an image and starts containers, which takes minutes rather than
/// seconds. It skips rather than fails when Docker is unavailable, so a developer without
/// a running daemon is not blocked.
/// </para>
/// </remarks>
[Trait("Category", "Smoke")]
[Collection(DeploymentGroup.Name)]
public sealed class ReferenceDeploymentSmokeTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string BaseAddress = ComposeDeployment.BaseAddress;

    private readonly ComposeDeployment _deployment = new();

    public async Task InitializeAsync()
    {
        if (!await _deployment.StartAsync().ConfigureAwait(false))
        {
            output.WriteLine("Docker is not available; smoke test will skip.");
            return;
        }

        // 019 FR-007: the release gate's evidence of which bytes it tested.
        output.WriteLine($"image: {_deployment.RanImage}");
    }

    public Task DisposeAsync() => _deployment.StopAsync();

    /// <summary>
    /// The whole operator journey against the real deployment.
    /// </summary>
    /// <remarks>
    /// Written as one test rather than several because the steps share an expensive
    /// container lifecycle and are meaningful only in sequence — a site must exist before
    /// it can be resolved. Each step reports through <c>ITestOutputHelper</c>, so a
    /// failure says which one broke.
    /// </remarks>
    [SkippableFact]
    public async Task Operator_journey_works_against_the_reference_deployment()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };

        // What the admin shell sends on every state-changing call (002 research R-11).
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        // 1. Readiness — the probe that means "migrations are done and the database is up".
        var readyIn = await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        output.WriteLine($"ready after {readyIn.TotalSeconds:F1}s");

        // 2. Liveness answers independently of readiness.
        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // 3. A fresh installation is in setup mode: nothing administrative answers until it
        //    is claimed, and there are no default credentials to try (002 FR-001, FR-005).
        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.True(status.GetProperty("setupRequired").GetBoolean());
        var gated = await client.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal((HttpStatusCode)428, gated.StatusCode);
        output.WriteLine("fresh install: setup required");

        // 4. The admin shell is served from the image, not from a separate process.
        var admin = await client.GetAsync(new Uri("/admin/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        output.WriteLine("admin shell served");

        // 5. The setup wizard, as an operator does it: the code comes from the logs.
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(true);
        var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Smoke Operator", email = "operator@smoke.test", password = "smoke test passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        var session = setup.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal))
            .Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", session);

        // SC-001 evidence, recorded rather than asserted: CI runners vary too much for a
        // hard limit, and the quickstart run measures it on the reference environment.
        output.WriteLine($"setup complete; signed in {_deployment.SinceUp.Elapsed.TotalSeconds:F1}s after compose up");

        // 5a. The admin surface refuses anyone without a session (002 FR-024, E-1 closed).
        using (var anonymous = new HttpClient { BaseAddress = new Uri(BaseAddress) })
        {
            var refused = await anonymous.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // 5b. Sign out, and the session is gone for good; sign in again with the same
        //     credentials (002 US2).
        var signedOut = await client.PostAsync(new Uri("/api/v1/identity/sign-out", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        var replayed = await client.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        var signIn = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/sign-in", UriKind.Relative),
                new { email = "operator@smoke.test", password = "smoke test passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add(
            "Cookie",
            signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
        output.WriteLine("anonymous refused; signed out and back in");

        // 6. As the new administrator: a fresh installation has no sites (FR-014a).
        var empty = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(0, empty.GetArrayLength());

        // 7. Create a site.
        var created = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Smoke", hostNames = new[] { "smoke.test" } })
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // 8. It appears in the list.
        var listed = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(1, listed.GetArrayLength());
        output.WriteLine("site created and listed");

        // 9. The public surface resolves it by host name — the end-to-end proof that site
        //    resolution works behind the real container, not just in-process. Visitors never
        //    authenticate, so this goes without the session.
        using var publicClient = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Host = "smoke.test";

        // Nothing is published yet, so the site answers with its own not-found page — not
        // 009's "no such site" — which is what resolving means until a page is live (012).
        var resolved = await publicClient.SendAsync(request).ConfigureAwait(true);

        Assert.True(await ResolvesAsync(resolved).ConfigureAwait(true), "smoke.test did not resolve to a site");
        output.WriteLine("public host resolved to the created site");

        // 9-bis. 009 FR-031: a second site; each host name reaches its own site; disabling one
        //        takes it off the public surface at once, and enabling brings it back.
        var second = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Second", hostNames = new[] { "second.smoke.test" } })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();

        Assert.True(await PublicResolvesAsync(publicClient, "smoke.test").ConfigureAwait(true));
        Assert.True(await PublicResolvesAsync(publicClient, "second.smoke.test").ConfigureAwait(true));

        var disabled = await client.PostAsync(new Uri($"/api/v1/sites/{secondId}/disable", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False(await PublicResolvesAsync(publicClient, "second.smoke.test").ConfigureAwait(true));
        Assert.True(await PublicResolvesAsync(publicClient, "smoke.test").ConfigureAwait(true));

        var enabled = await client.PostAsync(new Uri($"/api/v1/sites/{secondId}/enable", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        Assert.True(await PublicResolvesAsync(publicClient, "second.smoke.test").ConfigureAwait(true));
        output.WriteLine("two sites resolve separately; disable and enable take effect at once");

        // 9a. 003 FR-032: upload an image to the site and fetch it from the site's own public
        //     address, served by the real image from the real volume.
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        using var upload = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/media/sites/{siteId}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(Png),
        };
        upload.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        upload.Headers.Add("X-QuickBuilder-File-Name", "smoke.png");

        var uploaded = await client.SendAsync(upload).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var publicPath = (await uploaded.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("publicPath").GetString()!;

        Assert.Equal(Png, await FetchPublicAsync(publicClient, publicPath).ConfigureAwait(true));
        output.WriteLine($"image uploaded and served at {publicPath}");

        // 9b. 003 SC-005: replace the application container, keeping the volumes, as an
        //     upgrade does. The file and its record survive and serve identical bytes.
        var recreate = await _deployment.ComposeAsync("up -d --force-recreate --no-deps app").ConfigureAwait(true);
        Assert.True(recreate.ExitCode == 0, $"recreating the app container failed:\n{recreate.Output}");
        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);

        Assert.Equal(Png, await FetchPublicAsync(publicClient, publicPath).ConfigureAwait(true));
        output.WriteLine("image still served after the app container was replaced");

        // 9c. 010 FR-036: create a page and a child page, save a draft and read it back — the
        //     "create page" step of the constitution's journey. Publish and view follow in 9e.
        var pages = $"/api/v1/pages/sites/{siteId}/pages";
        var home = await client
            .PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Home", slug = "home", isHome = true })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, home.StatusCode);
        var homeId = (await home.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();

        var child = await client
            .PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "News", slug = "news", parentId = homeId })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, child.StatusCode);
        var childBody = await child.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal("/news", childBody.GetProperty("path").GetString());

        var draftPath = new Uri($"{pages}/{childBody.GetProperty("id").GetGuid()}/draft", UriKind.Relative);
        var draftVersion = (await client.GetFromJsonAsync<JsonElement>(draftPath).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        const string Document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";
        using var save = new StringContent($$"""{"version":{{draftVersion}},"document":{{Document}}}""", System.Text.Encoding.UTF8, "application/json");
        var saved = await client.PutAsync(draftPath, save).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var readBack = await client.GetFromJsonAsync<JsonElement>(draftPath).ConfigureAwait(true);
        using var expected = JsonDocument.Parse(Document);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, readBack.GetProperty("document")));
        output.WriteLine("page and child page created; draft saved and read back");

        // 9d. 011 FR-044 (plan exception E-5): the editor's route is served by the image, a
        //     draft holding a Section with a Heading — what the editor saves when a member adds
        //     them — is accepted and read back, and the site starts with the default design
        //     tokens. Driving the editor itself in a browser waits for the harness in #25.
        var editorPage = await client
            .GetAsync(new Uri($"/admin/sites/{siteId}/pages/{homeId}/edit", UriKind.Relative))
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, editorPage.StatusCode);
        Assert.Contains("<div id=\"app\"", await editorPage.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);

        var homeDraft = new Uri($"{pages}/{homeId}/draft", UriKind.Relative);
        var homeVersion = (await client.GetFromJsonAsync<JsonElement>(homeDraft).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        const string Blocks = """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s1","type":"section","props":{},"children":[
                {"id":"h1","type":"heading","props":{"text":"Welcome","level":1},"children":[]}]}]}}
            """;
        using var saveBlocks = new StringContent($$"""{"version":{{homeVersion}},"document":{{Blocks}}}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(homeDraft, saveBlocks).ConfigureAwait(true)).StatusCode);

        var blocks = (await client.GetFromJsonAsync<JsonElement>(homeDraft).ConfigureAwait(true)).GetProperty("document").GetProperty("root").GetProperty("children");
        Assert.Equal("section", blocks[0].GetProperty("type").GetString());
        Assert.Equal("heading", blocks[0].GetProperty("children")[0].GetProperty("type").GetString());

        var tokens = await client
            .GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/editor/sites/{siteId}/design-tokens", UriKind.Relative))
            .ConfigureAwait(true);
        Assert.True(tokens.GetProperty("isDefault").GetBoolean());
        output.WriteLine("editor route served; a Section with a Heading saved and read back; default design tokens");

        // 9e. 012 FR-043 (plan exception E-3 closed): publish and view. A Header component and
        //     the home page that shows it are published, and a visitor gets both from the
        //     site's host name, with the stylesheet the page links to. Unpublished, the page
        //     is gone at the next request.
        var components = $"/api/v1/pages/sites/{siteId}/components";
        var header = await client.PostAsJsonAsync(new Uri(components, UriKind.Relative), new { name = "Header" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, header.StatusCode);
        var headerId = (await header.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = $"{components}/{headerId}";
        var headerVersion = await SaveDraftAsync(client, headerPath, """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"hs","type":"section","props":{},"children":[
                {"id":"hh","type":"heading","props":{"text":"Smoke header","level":2},"children":[]}]}]}}
            """).ConfigureAwait(true);
        await PublishAsync(client, headerPath, headerVersion).ConfigureAwait(true);

        var homePath = $"{pages}/{homeId}";
        var homeWithHeader = await SaveDraftAsync(client, homePath, $$$"""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"ref","type":"component-ref","props":{"componentId":"{{{headerId}}}"},"children":[]},
              {"id":"s1","type":"section","props":{},"children":[
                {"id":"h1","type":"heading","props":{"text":"Welcome","level":1},"children":[]}]}]}}
            """).ConfigureAwait(true);
        await PublishAsync(client, homePath, homeWithHeader).ConfigureAwait(true);

        using var view = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        view.Headers.Host = "smoke.test";
        using var viewed = await publicClient.SendAsync(view).ConfigureAwait(true);
        var html = await viewed.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, viewed.StatusCode);
        Assert.Contains(">Welcome</h1>", html, StringComparison.Ordinal);
        Assert.Contains(">Smoke header</h2>", html, StringComparison.Ordinal);

        var stylesheet = System.Text.RegularExpressions.Regex.Match(html, "<link rel=\"stylesheet\" href=\"(?<href>[^\"]+)\"").Groups["href"].Value;
        using var css = new HttpRequestMessage(HttpMethod.Get, new Uri(stylesheet, UriKind.Relative));
        css.Headers.Host = "smoke.test";
        Assert.Equal(HttpStatusCode.OK, (await publicClient.SendAsync(css).ConfigureAwait(true)).StatusCode);

        // 9e2. 024: a menu is created and published, the header gains a Navigation block that
        //      shows it, and once the header is published again the home page carries the
        //      <nav>, with the policy admitting exactly its inline script.
        var menus = $"/api/v1/pages/sites/{siteId}/menus";
        using var menu = await client.PostAsJsonAsync(new Uri(menus, UriKind.Relative), new { name = "Main" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, menu.StatusCode);
        var menuRead = await menu.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var menuPath = $"{menus}/{menuRead.GetProperty("id").GetGuid()}";
        using var menuDraft = await client.PutAsJsonAsync(new Uri($"{menuPath}/draft", UriKind.Relative), new
        {
            version = menuRead.GetProperty("draftVersion").GetUInt32(),
            document = JsonElement.Parse("""{"schemaVersion":1,"entries":[{"id":"docs","label":"Smoke docs","target":{"type":"url","href":"https://docs.example/"},"children":[]}]}"""),
        }).ConfigureAwait(true);
        Assert.True(menuDraft.StatusCode == HttpStatusCode.OK, $"Saving the menu failed: {await menuDraft.Content.ReadAsStringAsync().ConfigureAwait(true)}");
        await PublishAsync(client, menuPath, (await menuDraft.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32()).ConfigureAwait(true);

        var headerWithMenu = await SaveDraftAsync(client, headerPath, $$$"""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"hs","type":"section","props":{},"children":[
                {"id":"hh","type":"heading","props":{"text":"Smoke header","level":2},"children":[]},
                {"id":"hn","type":"navigation","props":{"menuId":"{{{menuRead.GetProperty("id").GetGuid()}}}"},"children":[]}]}]}}
            """).ConfigureAwait(true);
        await PublishAsync(client, headerPath, headerWithMenu).ConfigureAwait(true);

        using var withMenu = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        withMenu.Headers.Host = "smoke.test";
        using var withMenuResponse = await publicClient.SendAsync(withMenu).ConfigureAwait(true);
        var menuHtml = await withMenuResponse.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Contains("<nav class=\"qb-nav", menuHtml, StringComparison.Ordinal);
        Assert.Contains(">Smoke docs</a>", menuHtml, StringComparison.Ordinal);
        Assert.Contains("script-src 'sha256-", string.Join(';', withMenuResponse.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        output.WriteLine("menu published and shown by the header's Navigation block on the home page");

        var unpublished = await client.PostAsync(new Uri($"{homePath}/unpublish", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        using var gone = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        gone.Headers.Host = "smoke.test";
        using var goneResponse = await publicClient.SendAsync(gone).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, goneResponse.StatusCode);
        Assert.Equal("text/html", goneResponse.Content.Headers.ContentType?.MediaType);
        output.WriteLine($"component and page published and viewed with {stylesheet}; unpublished page answers 404");

        // 9f. 013 FR-043: a published image is responsive. A JPEG is placed on the home page,
        //     the page is published, and the srcset a visitor receives names a resized variant
        //     that the image itself produces with libvips.
        using var jpeg = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/media/sites/{siteId}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(QuickBuilder.TestFixtures.Media.ImageFixtures.RotatedPhoto),
        };
        jpeg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        jpeg.Headers.Add("X-QuickBuilder-File-Name", "smoke-photo.jpg");
        var stored = await client.SendAsync(jpeg).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, stored.StatusCode);
        var photo = await stored.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal(800, photo.GetProperty("width").GetInt32());

        var homeWithImage = await SaveDraftAsync(client, homePath, $$$"""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s2","type":"section","props":{},"children":[
                {"id":"i1","type":"image","props":{"media":{"mediaId":"{{{photo.GetProperty("id").GetGuid()}}}"},"alt":"Smoke photo"},"children":[]}]}]}}
            """).ConfigureAwait(true);
        await PublishAsync(client, homePath, homeWithImage).ConfigureAwait(true);

        using var withImage = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        withImage.Headers.Host = "smoke.test";
        using var imagePage = await publicClient.SendAsync(withImage).ConfigureAwait(true);
        var imageHtml = await imagePage.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, imagePage.StatusCode);

        var srcset = System.Text.RegularExpressions.Regex.Match(imageHtml, "srcset=\"(?<set>[^\"]+)\"").Groups["set"].Value;
        Assert.False(string.IsNullOrEmpty(srcset), "The published image has no srcset.");
        var variantPath = srcset.Split(',')[0].Trim().Split(' ')[0].Replace("&amp;", "&", StringComparison.Ordinal);
        Assert.Contains("-w", variantPath, StringComparison.Ordinal);

        using var variant = new HttpRequestMessage(HttpMethod.Get, new Uri(variantPath, UriKind.Relative));
        variant.Headers.Host = "smoke.test";
        using var variantResponse = await publicClient.SendAsync(variant).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, variantResponse.StatusCode);
        Assert.Equal("image/jpeg", variantResponse.Content.Headers.ContentType?.MediaType);
        output.WriteLine($"published image offers {srcset}; {variantPath} served as image/jpeg");

        // 9g. 014 (plan, Principle X): the Cms module's migration ran in the image, and a
        //     collection with one text field is created and read back.
        var collections = $"/api/v1/cms/sites/{siteId}/collections";
        using var newCollection = await client.PostAsJsonAsync(new Uri(collections, UriKind.Relative), new { name = "Posts", slug = "posts" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, newCollection.StatusCode);
        var collectionId = (await newCollection.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        using var withField = await client.PutAsJsonAsync(
            new Uri($"{collections}/{collectionId}", UriKind.Relative),
            new
            {
                revision = 1,
                name = "Posts",
                slug = "posts",
                schema = new { schemaVersion = 1, fields = new[] { new { id = "~1", key = "title", name = "Title", help = "", required = true, type = "text", multiline = false, maxLength = 256 } } },
            }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, withField.StatusCode);
        var collectionRead = await client.GetFromJsonAsync<JsonElement>(new Uri($"{collections}/{collectionId}", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(("f1", 2), (collectionRead.GetProperty("schema").GetProperty("titleFieldId").GetString(), collectionRead.GetProperty("revision").GetInt32()));
        output.WriteLine($"collection {collectionId} created with field f1 at revision 2");

        // 9h. 015 (plan, Principle X): an item is created in that collection and published, as
        //     version 1.
        var items = $"{collections}/{collectionId}/items";
        using var newItem = await client.PostAsJsonAsync(new Uri(items, UriKind.Relative), new
        {
            slug = "hello-world",
            schemaRevision = collectionRead.GetProperty("schemaRevision").GetInt32(),
            values = new { schemaVersion = 1, values = new { f1 = "Hello world" } },
        }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, newItem.StatusCode);
        var item = await newItem.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var itemId = item.GetProperty("id").GetGuid();
        using var publishedItem = await client.PostAsJsonAsync(
            new Uri($"{items}/{itemId}/publish", UriKind.Relative),
            new { revision = item.GetProperty("revision").GetInt32(), cancelSchedule = false }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, publishedItem.StatusCode);
        var itemRead = await publishedItem.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal(("published", 1), (itemRead.GetProperty("status").GetString(), itemRead.GetProperty("liveVersion").GetInt32()));
        output.WriteLine($"item {itemId} created and published as version 1");

        // 9i. 016 (plan, Principle X): a page becomes the collection's template, and the
        //     published item answers at its own address with its title bound in.
        using var blog = await client.PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Blog", slug = "blog" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, blog.StatusCode);
        var blogId = (await blog.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        using var post = await client.PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Post", slug = "post", parentId = blogId }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var postRead = await post.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var postPath = $"{pages}/{postRead.GetProperty("id").GetGuid()}";
        using var template = await client.PutAsJsonAsync(
            new Uri($"{postPath}/template", UriKind.Relative),
            new { collectionId, version = postRead.GetProperty("version").GetInt32() }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);

        var templateDraft = await SaveDraftAsync(client, postPath, """
            {"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[
                {"id":"h","type":"heading","props":{"text":"Untitled","level":1,"bind":{"text":{"value":{"field":"f1"}}}},"children":[]}]}]}}
            """).ConfigureAwait(true);
        await PublishAsync(client, postPath, templateDraft).ConfigureAwait(true);

        using var itemPageRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/blog/hello-world", UriKind.Relative));
        itemPageRequest.Headers.Host = "smoke.test";
        using var itemPage = await publicClient.SendAsync(itemPageRequest).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, itemPage.StatusCode);
        Assert.Contains(">Hello world</h1>", await itemPage.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        output.WriteLine("template /blog/{slug} published; /blog/hello-world shows the item's title");

        // 9j. 017 (tasks T109): a redirect answers on the site's host name, and the sitemap and
        //     robots.txt describe the site.
        using var redirect = await client.PostAsJsonAsync(
            new Uri($"/api/v1/pages/sites/{siteId}/redirects", UriKind.Relative),
            new { source = "/old-post", match = "exact", target = new { kind = "path", path = "/blog/hello-world" }, statusCode = 301, enabled = true }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, redirect.StatusCode);
        using (var noFollow = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(BaseAddress) })
        {
            using var oldAddress = new HttpRequestMessage(HttpMethod.Get, new Uri("/old-post", UriKind.Relative));
            oldAddress.Headers.Host = "smoke.test";
            using var moved = await noFollow.SendAsync(oldAddress).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
            Assert.Equal("/blog/hello-world", moved.Headers.Location?.OriginalString);
        }

        using var sitemapRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/sitemap.xml", UriKind.Relative));
        sitemapRequest.Headers.Host = "smoke.test";
        using var sitemap = await publicClient.SendAsync(sitemapRequest).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, sitemap.StatusCode);
        Assert.Contains("/blog/hello-world</loc>", await sitemap.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        using var robotsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/robots.txt", UriKind.Relative));
        robotsRequest.Headers.Host = "smoke.test";
        using var robots = await publicClient.SendAsync(robotsRequest).ConfigureAwait(true);
        Assert.Contains("Sitemap: https://smoke.test/sitemap.xml", await robots.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        output.WriteLine("redirect /old-post → /blog/hello-world answers 301; sitemap.xml lists the item page; robots.txt names the sitemap");

        // 9k. 018 (tasks T092): a Form block on a contact page is published, a visitor submits
        //     it on the site's host name, and the submission is in Submissions. The form becomes
        //     known through the outbox, so the first submits may find it not live yet.
        var contact = await client
            .PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Contact", slug = "contact" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, contact.StatusCode);
        var contactId = (await contact.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var contactPath = $"{pages}/{contactId}";
        const string FormDocument = """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{},"children":[
              {"id":"contact-form","type":"form","props":{"name":"Contact","fields":[{"id":"e00000000001","type":"email","label":"Email","required":true}]},"children":[]}]}]}}
            """;
        await PublishAsync(client, contactPath, await SaveDraftAsync(client, contactPath, FormDocument).ConfigureAwait(true)).ConfigureAwait(true);

        var formPage = await PublicGetAsync(publicClient, "/contact").ConfigureAwait(true);
        Assert.Contains("action=\"/_forms/contact-form\"", formPage, StringComparison.Ordinal);

        HttpStatusCode submitted = default;
        for (var attempt = 0; attempt < 30 && submitted != HttpStatusCode.OK; attempt++)
        {
            using var submit = new HttpRequestMessage(HttpMethod.Post, new Uri("/_forms/contact-form", UriKind.Relative))
            {
                Content = new FormUrlEncodedContent([new("_page", contactId.ToString()), new("_path", "/contact"), new("f.e00000000001", "smoke-visitor@example.com")]),
            };
            submit.Headers.Host = "smoke.test";
            submit.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            using var answer = await publicClient.SendAsync(submit).ConfigureAwait(true);
            submitted = answer.StatusCode;
            if (submitted != HttpStatusCode.OK)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
            }
        }

        Assert.Equal(HttpStatusCode.OK, submitted);
        var inbox = await client.GetStringAsync(new Uri($"/api/v1/forms/sites/{siteId}/submissions", UriKind.Relative)).ConfigureAwait(true);
        Assert.Contains("smoke-visitor@example.com", inbox, StringComparison.Ordinal);
        output.WriteLine("a published form was submitted on smoke.test and the submission is in Submissions");

        // 10. Operator recovery (002 US5): with command-line access to the container, the
        //     operator issues a set-password link for the administrator, sets a new password
        //     through it, and signs in with that password.
        var recovery = await _deployment.ComposeAsync(
            "exec -T app dotnet QuickBuilder.Host.dll identity recovery-link --email operator@smoke.test").ConfigureAwait(true);
        Assert.True(recovery.ExitCode == 0, $"identity recovery-link failed:\n{recovery.Output}");

        var token = Uri.UnescapeDataString(
            recovery.Output.Split("#token=")[1].Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)[0]);

        using var recovered = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        recovered.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        var reset = await recovered
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/set-password", UriKind.Relative),
                new { token, newPassword = "recovered smoke passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var signInAgain = await recovered
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/sign-in", UriKind.Relative),
                new { email = "operator@smoke.test", password = "recovered smoke passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, signInAgain.StatusCode);
        output.WriteLine("recovery link issued from the command line; new password signs in");
    }

    /// <summary>A 1×1 PNG, the smallest real image the upload path accepts.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>Whether a host name reaches a site at all, whatever the site serves at <c>/</c>.</summary>
    /// <summary>Saves over the current draft of a page or component and returns the new draft version.</summary>
    private static async Task<uint> SaveDraftAsync(HttpClient client, string subject, string document)
    {
        var draft = new Uri($"{subject}/draft", UriKind.Relative);
        var version = (await client.GetFromJsonAsync<JsonElement>(draft).ConfigureAwait(false)).GetProperty("version").GetUInt32();
        using var body = new StringContent($$"""{"version":{{version}},"document":{{document}}}""", System.Text.Encoding.UTF8, "application/json");
        using var saved = await client.PutAsync(draft, body).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        return (await saved.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("version").GetUInt32();
    }

    private static async Task PublishAsync(HttpClient client, string subject, uint draftVersion)
    {
        using var published = await client
            .PostAsJsonAsync(new Uri($"{subject}/publish", UriKind.Relative), new { draftVersion })
            .ConfigureAwait(false);
        Assert.True(published.StatusCode == HttpStatusCode.OK, $"Publishing failed: {await published.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    }

    /// <summary>A public page of the smoke site, which must answer 200.</summary>
    private static async Task<string> PublicGetAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = "smoke.test";
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static async Task<bool> PublicResolvesAsync(HttpClient client, string hostName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Host = hostName;

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        return await ResolvesAsync(response).ConfigureAwait(false);
    }

    /// <summary>
    /// Reached a site: a page (200) or the site's own not-found page — anything but 009's
    /// <c>sites.not_resolved</c>, which unknown and disabled hosts get.
    /// </summary>
    private static async Task<bool> ResolvesAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.OK
        || (response.StatusCode == HttpStatusCode.NotFound
            && !(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("sites.not_resolved", StringComparison.Ordinal));

    private static async Task<byte[]> FetchPublicAsync(HttpClient client, string publicPath)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(publicPath, UriKind.Relative));
        request.Headers.Host = "smoke.test";

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// FR-036: the container runs as a non-root user.
    /// </summary>
    /// <remarks>
    /// Cheap to assert and easy to regress — a base-image bump or an added `USER root`
    /// for a build step would silently undo it, and nothing else would notice.
    /// </remarks>
    [SkippableFact]
    public async Task Application_container_runs_as_a_non_root_user()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        var id = await _deployment.ComposeAsync("exec -T app id -u").ConfigureAwait(true);

        Assert.Equal(0, id.ExitCode);
        Assert.NotEqual("0", id.Output.Trim());
        output.WriteLine($"container uid: {id.Output.Trim()}");
    }
}
