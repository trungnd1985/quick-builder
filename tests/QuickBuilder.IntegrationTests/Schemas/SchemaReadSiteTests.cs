using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Infrastructure.Schemas;

namespace QuickBuilder.IntegrationTests.Schemas;

/// <summary>
/// Stored documents leave their module at the current version, and one that is too new or
/// cannot be upgraded fails on its own, is never changed, and is logged without its content
/// (019 contracts/schema-upgrades.md §3, FR-021, FR-023a, FR-025, FR-026).
/// </summary>
/// <remarks>
/// Every contract is at version 1 today. "Too new" is staged by writing version 2 into rows
/// that carry no version check: published versions (pages, components, menus), design
/// versions and item values. Drafts, token sets and collections are held to version 1 by
/// <c>CHECK (schema_version = 1)</c>, so "cannot be upgraded" is staged instead by giving each
/// module a chain at version 2 whose only step throws: every stored version-1 document then
/// fails to upgrade.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SchemaReadSiteTests(DatabaseFixture database)
{
    private const string Secret = "Visible only to visitors";

    [Fact]
    public async Task A_too_new_document_fails_alone_and_is_never_changed()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "schema-too-new").ConfigureAwait(true);
        var admin = scenario.Admin;
        var site = scenario.SiteA;
        var pages = await PagesApi.SeedTreeAsync(admin, site, "home*\nolder\nother").ConfigureAwait(true);
        foreach (var (path, id) in pages)
        {
            await PublishingApi.SaveAsync(admin, PagesApi.PagePath(site, id), PublishingApi.HeadingDocument($"{Secret} {path}")).ConfigureAwait(true);
            await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(site, id)).ConfigureAwait(true);
        }

        var older = pages["/older"];
        var connection = scenario.App.ConnectionString;
        await DatabaseState.ExecuteAsync(connection, $"""
            UPDATE pages.published_versions
            SET schema_version = 2, document = jsonb_set(document, '{"{schemaVersion}"}', '2')
            WHERE subject_id = '{older}'
            """).ConfigureAwait(true);
        const string Row = "SELECT count(*) FROM pages.published_versions WHERE schema_version = 2 AND document->>'schemaVersion' = '2'";
        Assert.Equal(1, await DatabaseState.CountAsync(connection, Row).ConfigureAwait(true));

        // The public path: that page shows nothing of its content; its neighbour renders.
        using var visitor = scenario.App.CreateClientForHost(PagesScenario.HostA);
        var olderPage = await visitor.GetStringAsync(new Uri("/older", UriKind.Relative)).ConfigureAwait(true);
        Assert.DoesNotContain($"{Secret} /older", olderPage, StringComparison.Ordinal);
        Assert.Contains($"{Secret} /other", await visitor.GetStringAsync(new Uri("/other", UriKind.Relative)).ConfigureAwait(true), StringComparison.Ordinal);

        // The admin: refused, and refused again when asked to publish it back.
        var subject = PagesApi.PagePath(site, older);
        await AssertProblemAsync(await PublishingApi.VersionAsync(admin, subject, 1).ConfigureAwait(true), HttpStatusCode.Conflict, "pages.document_too_new").ConfigureAwait(true);
        await AssertProblemAsync(await PublishingApi.RollbackAsync(admin, subject, 1).ConfigureAwait(true), HttpStatusCode.Conflict, "pages.document_too_new").ConfigureAwait(true);
        Assert.Equal(1, await DatabaseState.CountAsync(connection, Row).ConfigureAwait(true));

        // A too-new published design: pages fall back to the default design and still render.
        var tokens = await EditorApi.GetOkAsync(admin, site).ConfigureAwait(true);
        using (var publish = await admin.PostAsJsonAsync(new Uri($"{EditorApi.TokensPath(site)}/publish", UriKind.Relative), new { version = tokens.GetProperty("version").GetUInt32() }).ConfigureAwait(true))
        {
            publish.EnsureSuccessStatusCode();
        }

        await DatabaseState.ExecuteAsync(connection, "UPDATE editor.design_versions SET schema_version = 2").ConfigureAwait(true);
        Assert.Contains($"{Secret} /other", await visitor.GetStringAsync(new Uri("/other", UriKind.Relative)).ConfigureAwait(true), StringComparison.Ordinal);

        // Too-new item values: the admin refuses the item.
        var collection = await CmsApi.CreateOkAsync(admin, site, "notes").ConfigureAwait(true);
        var collectionId = collection.GetProperty("id").GetGuid();
        var saved = await CmsApi.SaveOkAsync(admin, site, collectionId, CmsApi.SaveBody(collection, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
        })).ConfigureAwait(true);
        var item = await ItemsApi.CreateOkAsync(admin, site, collectionId,
            ItemsApi.Draft("a-note", new JsonObject { ["f1"] = Secret }, saved.GetProperty("schemaRevision").GetInt32())).ConfigureAwait(true);
        var itemId = ItemsApi.Id(item);
        await DatabaseState.ExecuteAsync(connection, $"UPDATE cms.items SET draft = jsonb_set(draft, '{{schemaVersion}}', '2') WHERE id = '{itemId}'").ConfigureAwait(true);
        await AssertProblemAsync(await ItemsApi.GetAsync(admin, site, collectionId, itemId).ConfigureAwait(true), HttpStatusCode.Conflict, "cms.document_too_new").ConfigureAwait(true);

        AssertLoggedWithoutContent(scenario.App.Logs, "schema_too_new");
    }

    [Fact]
    public async Task A_document_that_cannot_be_upgraded_is_refused_and_logged_without_content()
    {
        var connection = await database.CreateDatabaseAsync("schema-failed").ConfigureAwait(true);
        Guid site, page, menu, collection;
        using (var setUp = new QuickBuilderApp(connection))
        {
            var admin = await setUp.CreateAdminClientAsync().ConfigureAwait(true);
            site = await Media.MediaApi.CreateSiteAsync(admin, "Failing", "failing.test").ConfigureAwait(true);
            page = (await PagesApi.SeedTreeAsync(admin, site, "home*").ConfigureAwait(true))["/"];
            await PublishingApi.SaveAsync(admin, PagesApi.PagePath(site, page), PublishingApi.HeadingDocument(Secret)).ConfigureAwait(true);
            await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(site, page)).ConfigureAwait(true);
            menu = await MenusApi.CreateIdAsync(admin, site, "Main").ConfigureAwait(true);
            using (var tokens = await EditorApi.SaveCurrentAsync(admin, site, EditorApi.DefaultWithColor("c-secret", Secret)).ConfigureAwait(true))
            {
                tokens.EnsureSuccessStatusCode();
            }

            collection = (await CmsApi.CreateOkAsync(admin, site, "notes").ConfigureAwait(true)).GetProperty("id").GetGuid();
        }

        var before = await DatabaseState.FingerprintAsync(connection).ConfigureAwait(true);

        using var app = new QuickBuilderApp(connection, configureTestServices: services =>
        {
            services.Replace(ServiceDescriptor.Singleton(new QuickBuilder.Pages.Infrastructure.Schema.PageSchemaUpgrades(
                new SchemaUpgradeChain("page-schema", 2, [new Throws()]),
                QuickBuilder.Pages.Infrastructure.Schema.PageSchemaUpgrades.Create().Bindings)));
            services.Replace(ServiceDescriptor.Singleton(new QuickBuilder.Pages.Infrastructure.Schema.MenuSchemaUpgrades(
                new SchemaUpgradeChain("menu", 2, [new Throws()]))));
            services.Replace(ServiceDescriptor.Singleton(sp => new QuickBuilder.Editor.Infrastructure.DesignTokenUpgrades(
                new SchemaUpgradeChain("design-tokens", 2, [new Throws()]),
                sp.GetRequiredService<ILogger<QuickBuilder.Editor.Infrastructure.DesignTokenUpgrades>>())));
            services.Replace(ServiceDescriptor.Singleton(sp => new QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades(
                new SchemaUpgradeChain("collection-schema", 2, [new Throws()]),
                sp.GetRequiredService<ILogger<QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades>>())));
        });
        var client = await app.CreateAdminClientAsync().ConfigureAwait(true);

        await AssertProblemAsync(await PagesApi.DraftAsync(client, PagesApi.PagePath(site, page)).ConfigureAwait(true), HttpStatusCode.UnprocessableEntity, "pages.document_upgrade_failed").ConfigureAwait(true);
        await AssertProblemAsync(await MenusApi.GetAsync(client, site, menu).ConfigureAwait(true), HttpStatusCode.UnprocessableEntity, "pages.document_upgrade_failed").ConfigureAwait(true);
        await AssertProblemAsync(await EditorApi.GetAsync(client, site).ConfigureAwait(true), HttpStatusCode.UnprocessableEntity, "editor.document_upgrade_failed").ConfigureAwait(true);
        await AssertProblemAsync(await CmsApi.GetAsync(client, site, collection).ConfigureAwait(true), HttpStatusCode.UnprocessableEntity, "cms.document_upgrade_failed").ConfigureAwait(true);

        // The public path: the page shows nothing of its content rather than an old form.
        using var visitor = app.CreateClientForHost("failing.test");
        Assert.DoesNotContain(Secret, await visitor.GetStringAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true), StringComparison.Ordinal);

        AssertLoggedWithoutContent(app.Logs, "schema_upgrade_failed");

        // Reading wrote nothing, in any schema (FR-021: never on read).
        var after = await DatabaseState.FingerprintAsync(connection).ConfigureAwait(true);
        Assert.DoesNotContain(DatabaseState.Changed(before, after), t => !t.StartsWith("identity.", StringComparison.Ordinal));
    }

    /// <summary>A step that always throws, with content in its message that must not reach the logs.</summary>
    private sealed class Throws : ISchemaUpgradeStep
    {
        public int From => 1;

        public JsonObject Upgrade(JsonObject document) => throw new InvalidOperationException($"failed on {document.ToJsonString()}");
    }

    private static void AssertLoggedWithoutContent(LogCapture logs, string eventName)
    {
        var events = logs.Events
            .Where(e => e.Properties.TryGetValue("EventId", out var id) && id.ToString().Contains(eventName, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(events);
        Assert.All(events, e =>
        {
            var text = LogCapture.Render(e);
            Assert.Contains("StoredVersion", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        });
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(code, problem.RootElement.GetProperty("errorCode").GetString());
        }
    }
}
