using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>What one run of the content journey leaves for later releases to keep.</summary>
internal sealed record JourneyResult(HttpClient Admin, IReadOnlyList<ExpectedMember> Members, IReadOnlyList<ExpectedDraft> Drafts);

/// <summary>
/// Fills a deployment with one of every kind of stored data, through the admin API only, as a
/// member would (019 data-model §4, FR-011, FR-013).
/// </summary>
/// <remarks>
/// Each release's journey adds a site of its own, <c>release-&lt;version&gt;</c>, to the database
/// restored from the previous release, so the snapshot chain keeps content stored by every
/// earlier release (FR-014). Page and menu documents come from the contract golden examples
/// under <c>docs/contracts/</c>, with their placeholder ids replaced by real ones: a new block
/// or field type reaches the snapshot through its example.
/// <para>
/// Everything is invented: <c>example.test</c> addresses, fixed names, and passwords made up
/// for each run that exist only in the expectations file. Nothing is scheduled sooner than
/// <see cref="FarFuture"/>, so a snapshot restored months later shows what it showed when it
/// was taken (snapshot.md §3).
/// </para>
/// <para>
/// A feature that adds a kind of stored data adds a step here and a row to the list in
/// docs/contributing.md ("Adding stored data").
/// </para>
/// </remarks>
internal sealed class ContentJourney(ComposeDeployment deployment, string version)
{
    /// <summary>Schedules are set this far out: no gate ever reaches them.</summary>
    public static readonly DateTimeOffset FarFuture = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Placeholder ids the golden examples use, replaced with the ids this run creates.
    private const string ExampleComponent = "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f";
    private const string ExampleMedia = "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e30";
    private const string ExamplePage = "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20";
    private const string ExampleSeoImage = "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f";
    private const string ExampleConsentPage = "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e41";
    private const string ExampleRedirectPage = "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e42";
    private static readonly string[] ExampleMenuPages =
    [
        "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10",
        "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e11",
        "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e12",
    ];

    private readonly List<ExpectedMember> _members = [];
    private readonly List<ExpectedDraft> _drafts = [];

    /// <summary><c>release-0-2-0.example.test</c> for 0.2.0; one site per release.</summary>
    public static string HostFor(string version) =>
        $"release-{version.Replace('.', '-').Replace('+', '-')}.example.test".ToLowerInvariant();

    public string Host => HostFor(version);

    /// <summary>
    /// Runs the journey. On a fresh installation it completes the setup wizard; on a restored
    /// snapshot it signs in as <paramref name="administrator"/>, an Administrator an earlier
    /// journey created.
    /// </summary>
    public async Task<JourneyResult> RunAsync(ExpectedMember? administrator)
    {
        var admin = await SignInAsync(administrator).ConfigureAwait(false);

        // --- Sites, with a second host name and a disabled neighbour (009) -----------------
        var site = await CreateSiteAsync(admin, $"Release {version}", [Host, $"www.{Host}"]).ConfigureAwait(false);
        var off = await CreateSiteAsync(admin, $"Release {version} (disabled)", [$"off.{Host}"]).ConfigureAwait(false);
        await Ok(admin.PostAsync(new Uri($"/api/v1/sites/{off}/disable", UriKind.Relative), null), "disabling a site").ConfigureAwait(false);

        // --- Members with each role (002) --------------------------------------------------
        await CreateMemberAsync(admin, "administrator", new { administrator = true, siteRoles = Array.Empty<object>() }, "Administrator").ConfigureAwait(false);
        await CreateMemberAsync(admin, "designer", new { administrator = false, siteRoles = new[] { new { siteId = site, role = "Designer" } } }, "Designer").ConfigureAwait(false);
        await CreateMemberAsync(admin, "editor", new { administrator = false, siteRoles = new[] { new { siteId = site, role = "Editor" } } }, "Editor").ConfigureAwait(false);

        // --- Media: an image with variants, and a document (003, 013) -----------------------
        var photo = await UploadAsync(admin, site, TestFixtures.Media.ImageFixtures.RotatedPhoto, "image/jpeg", "release-photo.jpg").ConfigureAwait(false);
        var brochure = await UploadAsync(admin, site, Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n"), "application/pdf", "release-brochure.pdf").ConfigureAwait(false);

        // --- Pages, a shared component, history (010-012) -----------------------------------
        var pages = $"/api/v1/pages/sites/{site}/pages";
        var components = $"/api/v1/pages/sites/{site}/components";

        var header = await CreateAsync(admin, components, new { name = "Header" }).ConfigureAwait(false);
        var headerPath = $"{components}/{header}";
        await SavePublishAsync(admin, headerPath, Heading($"Release {version} header", 2)).ConfigureAwait(false);
        _drafts.Add(new ExpectedDraft("component", $"{headerPath}/draft"));

        var home = await CreateAsync(admin, pages, new { title = "Home", slug = "home", isHome = true }).ConfigureAwait(false);
        var about = await CreateAsync(admin, pages, new { title = "About", slug = "about", parentId = home }).ConfigureAwait(false);
        var news = await CreateAsync(admin, pages, new { title = "News", slug = "news" }).ConfigureAwait(false);
        var draftOnly = await CreateAsync(admin, pages, new { title = "Draft only", slug = "draft-only" }).ConfigureAwait(false);

        // About first: the home page links to it, and a link must point at a live page.
        await SavePublishAsync(admin, $"{pages}/{about}", Example("page-schema", "rich-text-every-format.json", (ExampleMedia, photo))).ConfigureAwait(false);
        await SavePublishAsync(admin, $"{pages}/{home}", Example(
            "page-schema", "every-block.json", (ExampleComponent, header), (ExampleMedia, photo), (ExamplePage, about))).ConfigureAwait(false);

        // News: two versions, rolled back to the first, then an unpublished change on top.
        var newsPath = $"{pages}/{news}";
        await SavePublishAsync(admin, newsPath, Heading("News, first edition", 1)).ConfigureAwait(false);
        await SavePublishAsync(admin, newsPath, Heading("News, second edition", 1)).ConfigureAwait(false);
        await Ok(admin.PostAsJsonAsync(new Uri($"{newsPath}/rollback", UriKind.Relative), new { versionNumber = 1 }), "rolling back a page").ConfigureAwait(false);
        await SaveDraftAsync(admin, newsPath, Heading("News, unpublished third edition", 1)).ConfigureAwait(false);
        _drafts.Add(new ExpectedDraft("page", $"{newsPath}/draft"));

        await SaveDraftAsync(admin, $"{pages}/{draftOnly}", Heading("Never published", 1)).ConfigureAwait(false);
        _drafts.Add(new ExpectedDraft("page", $"{pages}/{draftOnly}/draft"));

        // --- Design with a menu style (011, 024) -------------------------------------------
        var tokens = $"/api/v1/editor/sites/{site}/design-tokens";
        var tokenVersion = (await admin.GetFromJsonAsync<JsonElement>(new Uri(tokens, UriKind.Relative)).ConfigureAwait(false)).GetProperty("version").GetUInt32();
        var savedTokens = await Ok(
            PutRawAsync(admin, tokens, $$"""{"version":{{tokenVersion}},"document":{{Example("design-tokens", "menu-style.json")}}}"""),
            "saving design tokens").ConfigureAwait(false);
        await Ok(
            admin.PostAsJsonAsync(new Uri($"{tokens}/publish", UriKind.Relative), new { version = savedTokens.GetProperty("version").GetUInt32() }),
            "publishing the design").ConfigureAwait(false);
        _drafts.Add(new ExpectedDraft("design", tokens));

        // --- A three-level menu shown by a Navigation block (024) ---------------------------
        var menus = $"/api/v1/pages/sites/{site}/menus";
        var menu = await Ok(admin.PostAsJsonAsync(new Uri(menus, UriKind.Relative), new { name = "Main" }), "creating a menu").ConfigureAwait(false);
        var menuId = menu.GetProperty("id").GetGuid();
        var menuPath = $"{menus}/{menuId}";
        var menuDocument = Example("menu", "three-levels.json", (ExampleMenuPages[0], home), (ExampleMenuPages[1], about), (ExampleMenuPages[2], news));
        var menuSaved = await Ok(
            PutRawAsync(admin, $"{menuPath}/draft", $$"""{"version":{{menu.GetProperty("draftVersion").GetUInt32()}},"document":{{menuDocument}}}"""),
            "saving a menu").ConfigureAwait(false);
        await Ok(
            admin.PostAsJsonAsync(new Uri($"{menuPath}/publish", UriKind.Relative), new { draftVersion = menuSaved.GetProperty("draftVersion").GetUInt32() }),
            "publishing a menu").ConfigureAwait(false);
        _drafts.Add(new ExpectedDraft("menu", menuPath));

        var navigation = await CreateAsync(admin, pages, new { title = "Sitemap", slug = "sitemap-page" }).ConfigureAwait(false);
        await SavePublishAsync(admin, $"{pages}/{navigation}", $$$"""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[
                {"id":"n","type":"navigation","props":{"menuId":"{{{menuId}}}"},"children":[]}]}]}}
            """).ConfigureAwait(false);

        // --- SEO: every field, a redirect (017) -------------------------------------------
        var seo = await CreateAsync(admin, pages, new { title = "Search", slug = "search-ready" }).ConfigureAwait(false);
        await SavePublishAsync(admin, $"{pages}/{seo}", Example("page-schema", "seo-page-all-fields.json", (ExampleSeoImage, photo))).ConfigureAwait(false);
        await Ok(
            admin.PostAsJsonAsync(
                new Uri($"/api/v1/pages/sites/{site}/redirects", UriKind.Relative),
                new { source = "/old-about", match = "exact", target = new { kind = "page", pageId = about }, statusCode = 301, enabled = true }),
            "creating a redirect").ConfigureAwait(false);

        // --- Collections, items, a template and a list (014-016) --------------------------
        await CollectionsAsync(admin, site, pages, photo, brochure).ConfigureAwait(false);

        // --- A form with every field type, its submissions and spam (018) ------------------
        var contact = await CreateAsync(admin, pages, new { title = "Contact", slug = "contact" }).ConfigureAwait(false);
        await SavePublishAsync(admin, $"{pages}/{contact}", Example(
            "page-schema", "form-every-field-type.json", (ExampleConsentPage, about), (ExampleRedirectPage, about))).ConfigureAwait(false);
        await SubmitFormsAsync(admin, site, contact).ConfigureAwait(false);

        return new JourneyResult(admin, _members, _drafts);
    }

    /// <summary>Two collections: authors, and articles with one field of every type.</summary>
    private async Task CollectionsAsync(HttpClient admin, Guid site, string pages, Guid photo, Guid brochure)
    {
        var collections = $"/api/v1/cms/sites/{site}/collections";

        var authors = await CreateAsync(admin, collections, new { name = "Authors", slug = "authors" }).ConfigureAwait(false);
        await SaveSchemaAsync(admin, $"{collections}/{authors}", "Authors", "authors", new JsonArray(
            Field("~1", "name", "text", required: true, new { multiline = false, maxLength = 200 }))).ConfigureAwait(false);
        var author = await CreateItemAsync(admin, $"{collections}/{authors}", "ada", new JsonObject { ["name"] = "Ada Example" }, publish: true).ConfigureAwait(false);

        var articles = await CreateAsync(admin, collections, new { name = "Articles", slug = "articles" }).ConfigureAwait(false);
        var articlesPath = $"{collections}/{articles}";
        await SaveSchemaAsync(admin, articlesPath, "Articles", "articles", new JsonArray(
            Field("~1", "title", "text", required: true, new { multiline = false, maxLength = 256 }),
            Field("~2", "summary", "text", required: false, new { multiline = true, maxLength = 2000 }),
            Field("~3", "body", "richText"),
            Field("~4", "reading-time", "number", required: false, new { decimals = 0, min = 0, max = 600 }),
            Field("~5", "featured", "boolean"),
            Field("~6", "published-on", "date", required: false, new { withTime = false }),
            Field("~7", "starts-at", "date", required: false, new { withTime = true }),
            Field("~8", "cover", "image"),
            Field("~9", "brochure", "file", required: false, new { accept = "documents" }),
            Field("~10", "category", "option", required: false, new { options = new[] { new { id = "~11", label = "News" }, new { id = "~12", label = "Guides" } } }),
            Field("~13", "author", "reference", required: false, new { target = authors }),
            Field("~14", "related", "multiReference", required: false, new { target = authors }))).ConfigureAwait(false);

        var full = new JsonObject
        {
            ["title"] = $"Every field, {version}",
            ["summary"] = "Two\nlines",
            ["body"] = JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"An article body."}]}]}"""),
            ["reading-time"] = 7,
            ["featured"] = true,
            ["published-on"] = "2026-10-08",
            ["starts-at"] = "2026-10-08T09:30:00Z",
            ["cover"] = new JsonObject { ["mediaId"] = photo.ToString() },
            ["brochure"] = new JsonObject { ["mediaId"] = brochure.ToString() },
            ["category"] = "o2",
            ["author"] = author.ToString(),
            ["related"] = new JsonArray(author.ToString()),
        };
        await CreateItemAsync(admin, articlesPath, "every-field", full, publish: true).ConfigureAwait(false);
        await CreateItemAsync(admin, articlesPath, "second", new JsonObject { ["title"] = "Second article", ["category"] = "o1" }, publish: true).ConfigureAwait(false);
        var draft = await CreateItemAsync(admin, articlesPath, "unpublished", new JsonObject { ["title"] = "Not yet" }, publish: false).ConfigureAwait(false);
        _drafts.Add(new ExpectedDraft("item", $"{articlesPath}/items/{draft}"));
        _drafts.Add(new ExpectedDraft("collection", articlesPath));

        var scheduled = await CreateItemAsync(admin, articlesPath, "scheduled", new JsonObject { ["title"] = "Far future" }, publish: false).ConfigureAwait(false);
        var scheduledRead = await admin.GetFromJsonAsync<JsonElement>(new Uri($"{articlesPath}/items/{scheduled}", UriKind.Relative)).ConfigureAwait(false);
        await Ok(
            admin.PutAsJsonAsync(
                new Uri($"{articlesPath}/items/{scheduled}/schedules/publish", UriKind.Relative),
                new { dueAtUtc = FarFuture, revision = scheduledRead.GetProperty("revision").GetInt32(), recapture = false }),
            "scheduling an item").ConfigureAwait(false);

        // An item template, its title bound in, and a list of articles with filters and paging.
        var ids = await FieldIdsAsync(admin, articlesPath).ConfigureAwait(false);
        var blog = await CreateAsync(admin, pages, new { title = "Articles", slug = "articles" }).ConfigureAwait(false);
        var post = await Ok(admin.PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Article", slug = "article", parentId = blog }), "creating the template page").ConfigureAwait(false);
        var postPath = $"{pages}/{post.GetProperty("id").GetGuid()}";
        await Ok(
            admin.PutAsJsonAsync(new Uri($"{postPath}/template", UriKind.Relative), new { collectionId = articles, version = post.GetProperty("version").GetInt32() }),
            "making a template").ConfigureAwait(false);
        await SavePublishAsync(admin, postPath, """
            {"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[
                {"id":"h","type":"heading","props":{"text":"Untitled","level":1,"bind":{"text":{"value":{"field":"__TITLE__"}}}},"children":[]},
                {"id":"t","type":"text","props":{"text":"","bind":{"text":{"value":{"field":"__SUMMARY__"}}}},"children":[]}]}]}}
            """.Replace("__TITLE__", ids["title"], StringComparison.Ordinal).Replace("__SUMMARY__", ids["summary"], StringComparison.Ordinal)).ConfigureAwait(false);
        await SavePublishAsync(admin, $"{pages}/{blog}", """
            {"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[
                {"id":"list","type":"collection-list","props":{
                  "source":{"collectionId":"__ARTICLES__"},
                  "filters":[{"field":"__TITLE__","op":"notEmpty"},{"field":"__CATEGORY__","op":"ne","value":"o3"}],
                  "sort":[{"item":"slug","dir":"asc"}],
                  "paging":{"pageSize":1,"param":"page"}},
                  "children":[
                    {"id":"card","type":"list-card","props":{},"children":[
                      {"id":"ct","type":"heading","props":{"text":"","level":3,"bind":{"text":{"value":{"field":"__TITLE__"}}}},"children":[]},
                      {"id":"cl","type":"button","props":{"label":"Read","bind":{"link":{"value":{"item":"url"}}}},"children":[]}]},
                    {"id":"empty","type":"list-empty","props":{},"children":[
                      {"id":"et","type":"text","props":{"text":"No articles."},"children":[]}]}]}]}]}}
            """
            .Replace("__TITLE__", ids["title"], StringComparison.Ordinal)
            .Replace("__CATEGORY__", ids["category"], StringComparison.Ordinal)
            .Replace("__ARTICLES__", articles.ToString(), StringComparison.Ordinal)).ConfigureAwait(false);
    }

    private async Task SubmitFormsAsync(HttpClient admin, Guid site, Guid contactPage)
    {
        using var document = JsonDocument.Parse(Example("page-schema", "form-every-field-type.json", (ExampleConsentPage, Guid.Empty), (ExampleRedirectPage, Guid.Empty)));
        var form = FindForm(document.RootElement.GetProperty("root"))!.Value;
        var formId = form.GetProperty("id").GetString()!;

        using var visitor = ReleaseExpectations.Visitor();
        foreach (var (email, trap) in new[] { ("first@visitor.example.test", string.Empty), ("second@visitor.example.test", string.Empty), ("bot@visitor.example.test", "I am a bot") })
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new("_page", contactPage.ToString()),
                new("_path", "/contact"),
                new("_hp", trap),
            };
            fields.AddRange(Answers(form, email));

            HttpStatusCode status = default;
            for (var attempt = 0; attempt < 30 && status != HttpStatusCode.OK; attempt++)
            {
                using var submit = new HttpRequestMessage(HttpMethod.Post, new Uri($"/_forms/{formId}", UriKind.Relative))
                {
                    Content = new FormUrlEncodedContent(fields),
                };
                submit.Headers.Host = Host;
                submit.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                using var answer = await visitor.SendAsync(submit).ConfigureAwait(false);
                status = answer.StatusCode;
                if (status != HttpStatusCode.OK)
                {
                    // The form becomes known through the outbox; the first submits may be early.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
            }

            Assert.True(status == HttpStatusCode.OK, $"Submitting the form as {email} answered {status}.");
        }

        var inbox = await admin.GetStringAsync(new Uri($"/api/v1/forms/sites/{site}/submissions", UriKind.Relative)).ConfigureAwait(false);
        Assert.Contains("first@visitor.example.test", inbox, StringComparison.Ordinal);
    }

    /// <summary>A valid answer for every field of the example form.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Answers(JsonElement form, string email)
    {
        foreach (var field in form.GetProperty("props").GetProperty("fields").EnumerateArray())
        {
            var name = $"f.{field.GetProperty("id").GetString()}";
            var value = field.GetProperty("type").GetString() switch
            {
                "email" => email,
                "phone" => "+44 20 7946 0000",
                "number" => "2",
                "date" => "2026-10-08",
                "url" => "https://example.test/",
                "consent" or "checkbox" => "on",
                "select" or "radio" or "checkboxes" => field.GetProperty("options")[0].GetProperty("value").GetString()!,
                _ => "An answer",
            };
            yield return new(name, value);
        }
    }

    private static JsonElement? FindForm(JsonElement node)
    {
        if (node.GetProperty("type").GetString() == "form")
        {
            return node;
        }

        foreach (var child in node.GetProperty("children").EnumerateArray())
        {
            if (FindForm(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private async Task<HttpClient> SignInAsync(ExpectedMember? administrator)
    {
        var admin = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };
        admin.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        var status = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(false);
        HttpResponseMessage response;
        if (status.GetProperty("setupRequired").GetBoolean())
        {
            var member = new ExpectedMember($"owner@{Host}", NewPassword(), "Administrator");
            var setupCode = await deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
            response = await admin.PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Release Owner", email = member.Email, password = member.Password }).ConfigureAwait(false);
            _members.Add(member);
        }
        else
        {
            Assert.True(administrator is not null, "The installation is already set up, and no Administrator from an earlier snapshot was given.");
            response = await SignInAsync(admin, administrator).ConfigureAwait(false);
        }

        Assert.True(response.IsSuccessStatusCode, $"Signing in failed: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        UseSession(admin, response);
        return admin;
    }

    /// <summary>Signs <paramref name="member"/> in on <paramref name="client"/>.</summary>
    public static Task<HttpResponseMessage> SignInAsync(HttpClient client, ExpectedMember member)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(member);
        return client.PostAsJsonAsync(new Uri("/api/v1/identity/sign-in", UriKind.Relative), new { email = member.Email, password = member.Password });
    }

    /// <summary>Carries the session a sign-in or setup response set on later requests.</summary>
    public static void UseSession(HttpClient client, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(response);
        var session = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0];
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", session);
    }

    private async Task CreateMemberAsync(HttpClient admin, string name, object grants, string role)
    {
        var member = new ExpectedMember($"{name}@{Host}", NewPassword(), role);
        var created = await Ok(
            admin.PostAsJsonAsync(new Uri("/api/v1/identity/users", UriKind.Relative), new { displayName = $"{role} Example", email = member.Email, grants }),
            $"creating the {role}").ConfigureAwait(false);
        var token = created.GetProperty("setPasswordLink").GetProperty("token").GetString()!;

        using var anonymous = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };
        anonymous.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        using var set = await anonymous.PostAsJsonAsync(new Uri("/api/v1/identity/set-password", UriKind.Relative), new { token, newPassword = member.Password }).ConfigureAwait(false);
        Assert.True(set.IsSuccessStatusCode, $"Setting the {role}'s password failed: {await set.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        _members.Add(member);
    }

    private static async Task<Guid> CreateSiteAsync(HttpClient admin, string name, string[] hostNames) =>
        (await Ok(admin.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name, hostNames }), $"creating site {name}").ConfigureAwait(false))
            .GetProperty("id").GetGuid();

    private static async Task<Guid> CreateAsync(HttpClient admin, string collection, object body) =>
        (await Ok(admin.PostAsJsonAsync(new Uri(collection, UriKind.Relative), body), $"creating under {collection}").ConfigureAwait(false))
            .GetProperty("id").GetGuid();

    private static async Task<Guid> UploadAsync(HttpClient admin, Guid site, byte[] bytes, string contentType, string fileName)
    {
        using var upload = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/media/sites/{site}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        upload.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        upload.Headers.Add("X-QuickBuilder-File-Name", fileName);
        return (await Ok(admin.SendAsync(upload), $"uploading {fileName}").ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    private static async Task SaveSchemaAsync(HttpClient admin, string collectionPath, string name, string slug, JsonArray fields)
    {
        var current = await admin.GetFromJsonAsync<JsonElement>(new Uri(collectionPath, UriKind.Relative)).ConfigureAwait(false);
        var body = new JsonObject
        {
            ["revision"] = current.GetProperty("revision").GetInt32(),
            ["name"] = name,
            ["slug"] = slug,
            ["schema"] = new JsonObject { ["schemaVersion"] = 1, ["fields"] = fields },
        };
        await Ok(PutRawAsync(admin, collectionPath, body.ToJsonString()), $"saving the {name} schema").ConfigureAwait(false);
    }

    /// <summary>Field key → stored field id, from the saved schema.</summary>
    private static async Task<Dictionary<string, string>> FieldIdsAsync(HttpClient admin, string collectionPath)
    {
        var collection = await admin.GetFromJsonAsync<JsonElement>(new Uri(collectionPath, UriKind.Relative)).ConfigureAwait(false);
        return collection.GetProperty("schema").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("key").GetString()!, f => f.GetProperty("id").GetString()!, StringComparer.Ordinal);
    }

    /// <summary>Creates an item from values keyed by field key, and publishes it if asked.</summary>
    private static async Task<Guid> CreateItemAsync(HttpClient admin, string collectionPath, string slug, JsonObject byKey, bool publish)
    {
        var collection = await admin.GetFromJsonAsync<JsonElement>(new Uri(collectionPath, UriKind.Relative)).ConfigureAwait(false);
        var ids = collection.GetProperty("schema").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("key").GetString()!, f => f.GetProperty("id").GetString()!, StringComparer.Ordinal);

        var values = new JsonObject();
        foreach (var (key, value) in byKey)
        {
            values[ids[key]] = value?.DeepClone();
        }

        var body = new JsonObject
        {
            ["slug"] = slug,
            ["schemaRevision"] = collection.GetProperty("schemaRevision").GetInt32(),
            ["values"] = new JsonObject { ["schemaVersion"] = 1, ["values"] = values },
        };
        var item = await Ok(PostRawAsync(admin, $"{collectionPath}/items", body.ToJsonString()), $"creating item {slug}").ConfigureAwait(false);
        var id = item.GetProperty("id").GetGuid();

        if (publish)
        {
            await Ok(
                admin.PostAsJsonAsync(
                    new Uri($"{collectionPath}/items/{id}/publish", UriKind.Relative),
                    new { revision = item.GetProperty("revision").GetInt32(), cancelSchedule = false }),
                $"publishing item {slug}").ConfigureAwait(false);
        }

        return id;
    }

    private static JsonObject Field(string id, string key, string type, bool required = false, object? settings = null)
    {
        var field = new JsonObject { ["id"] = id, ["key"] = key, ["name"] = key, ["help"] = string.Empty, ["required"] = required, ["type"] = type };
        foreach (var (name, value) in JsonSerializer.SerializeToNode(settings ?? new { })!.AsObject().ToList())
        {
            field[name] = value?.DeepClone();
        }

        return field;
    }

    private static async Task<uint> SaveDraftAsync(HttpClient admin, string subject, string document)
    {
        var draft = new Uri($"{subject}/draft", UriKind.Relative);
        var current = (await admin.GetFromJsonAsync<JsonElement>(draft).ConfigureAwait(false)).GetProperty("version").GetUInt32();
        var saved = await Ok(PutRawAsync(admin, draft.OriginalString, $$"""{"version":{{current}},"document":{{document}}}"""), $"saving {subject}").ConfigureAwait(false);
        return saved.GetProperty("version").GetUInt32();
    }

    private static async Task SavePublishAsync(HttpClient admin, string subject, string document)
    {
        var draftVersion = await SaveDraftAsync(admin, subject, document).ConfigureAwait(false);
        await Ok(admin.PostAsJsonAsync(new Uri($"{subject}/publish", UriKind.Relative), new { draftVersion }), $"publishing {subject}").ConfigureAwait(false);
    }

    private static string Heading(string text, int level) =>
        $$$"""
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"s","type":"section","props":{},"children":[
            {"id":"h","type":"heading","props":{"text":{{{JsonSerializer.Serialize(text)}}},"level":{{{level}}}},"children":[]}]}]}}
        """;

    /// <summary>A golden example from docs/contracts, with placeholder ids replaced.</summary>
    private string Example(string contract, string file, params (string Placeholder, Guid Id)[] replacements)
    {
        var text = File.ReadAllText(Path.Combine(deployment.RepositoryRoot, "docs", "contracts", contract, "examples", "valid", file));
        foreach (var (placeholder, id) in replacements)
        {
            text = text.Replace(placeholder, id.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static string NewPassword() =>
        "release gate " + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));

    private static Task<HttpResponseMessage> PutRawAsync(HttpClient client, string path, string json) =>
        SendRawAsync(client, HttpMethod.Put, path, json);

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json) =>
        SendRawAsync(client, HttpMethod.Post, path, json);

    private static async Task<HttpResponseMessage> SendRawAsync(HttpClient client, HttpMethod method, string path, string json)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    /// <summary>Awaits a call, fails with its body unless it succeeded, and returns its JSON (or an empty object).</summary>
    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call, string what)
    {
        using var response = await call.ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{what} answered {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return document.RootElement.Clone();
    }
}
