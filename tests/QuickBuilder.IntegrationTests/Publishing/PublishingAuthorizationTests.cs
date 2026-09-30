using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// Designers publish; Editors prepare drafts (012 clarification Q3, FR-009, FR-036, research PR-18).
/// </summary>
/// <remarks>
/// Every publishing operation, table-driven, for each role: allowed, forbidden on a site the
/// caller has a role on, or "no such site" for a caller with no role there (002).
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PublishingAuthorizationTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _page;
    private Guid _component;

    public enum Caller
    {
        Administrator,
        Designer,
        Editor,
        NoGrant,
    }

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "pub-authz").ConfigureAwait(false);
        _page = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(false))["/about"];
        _component = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Header").ConfigureAwait(false))
            .GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Operations only a publisher may run, with the status a publisher gets.</summary>
    private IEnumerable<(string Name, Func<HttpClient, Task<HttpResponseMessage>> Request, HttpStatusCode Success)> PublishingOperations()
    {
        var page = PagesApi.PagePath(_scenario.SiteA, _page);
        var component = PagesApi.ComponentPath(_scenario.SiteA, _component);

        foreach (var (kind, path) in new[] { ("page", page), ("component", component) })
        {
            yield return ($"publish {kind}", async c => await PublishingApi.PublishAsync(
                c, path, await PublishingApi.DraftVersionAsync(_scenario.Admin, path).ConfigureAwait(false)).ConfigureAwait(false), HttpStatusCode.OK);
            yield return ($"rollback {kind}", c => PublishingApi.RollbackAsync(c, path, 1), HttpStatusCode.OK);
            yield return ($"unpublish {kind}", c => PublishingApi.UnpublishAsync(c, path), HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData(Caller.Administrator, true)]
    [InlineData(Caller.Designer, true)]
    [InlineData(Caller.Editor, false)]
    public async Task Only_publishers_may_publish(Caller caller, bool allowed)
    {
        var client = await ClientAsync(caller).ConfigureAwait(true);

        foreach (var (name, request, success) in PublishingOperations())
        {
            var response = await request(client).ConfigureAwait(true);

            if (allowed)
            {
                Assert.True(response.StatusCode == success,
                    $"{name}: expected {(int)success}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(true)}");
            }
            else
            {
                await PagesApi.RefusedAsync(response, HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(true);
            }
        }
    }

    [Fact]
    public async Task A_caller_without_a_role_on_the_site_is_told_it_does_not_exist()
    {
        var client = await ClientAsync(Caller.NoGrant).ConfigureAwait(true);

        foreach (var (_, request, _) in PublishingOperations())
        {
            await PagesApi.RefusedAsync(await request(client).ConfigureAwait(true), HttpStatusCode.NotFound, "sites.not_found").ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(Caller.Administrator, true, true)]
    [InlineData(Caller.Designer, true, false)]
    [InlineData(Caller.Editor, false, false)]
    public async Task The_tree_tells_each_role_what_it_may_do(Caller caller, bool canPublish, bool canViewLog)
    {
        var client = await ClientAsync(caller).ConfigureAwait(true);
        var permissions = (await PagesApi.TreeOkAsync(client, _scenario.SiteA).ConfigureAwait(true)).GetProperty("permissions");

        Assert.Equal(canPublish, permissions.GetProperty("canPublish").GetBoolean());
        Assert.Equal(canViewLog, permissions.GetProperty("canViewPublishLog").GetBoolean());
    }

    private Task<HttpClient> ClientAsync(Caller caller) => caller switch
    {
        Caller.Administrator => Task.FromResult(_scenario.Admin),
        Caller.Designer => _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)),
        Caller.Editor => _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)),
        _ => _scenario.UserAsync((_scenario.SiteB, SystemRole.Designer)),
    };
}
