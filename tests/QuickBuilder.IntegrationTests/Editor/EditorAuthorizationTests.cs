using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Who may read and change a site's design tokens: every site role reads them, Designer
/// changes them (011 FR-040, FR-041, research ED-20).
/// </summary>
/// <remarks>
/// A role on the site that lacks the capability is told 403; a user with no role on the site
/// is told the site does not exist (002).
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class EditorAuthorizationTests(DatabaseFixture database)
{
    public enum Caller
    {
        Administrator,
        Designer,
        Editor,
        NoGrant,
    }

    [Theory]
    [InlineData(Caller.Administrator, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(Caller.Designer, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(Caller.Editor, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData(Caller.NoGrant, HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    public async Task Each_role_gets_exactly_its_capabilities(Caller caller, HttpStatusCode read, HttpStatusCode save)
    {
        using var scenario = await PagesScenario.CreateAsync(database, "ed-authz").ConfigureAwait(true);
        var client = caller switch
        {
            Caller.Administrator => scenario.Admin,
            Caller.Designer => await scenario.UserAsync((scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true),
            Caller.Editor => await scenario.UserAsync((scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true),
            _ => await scenario.UserAsync((scenario.SiteB, SystemRole.Designer)).ConfigureAwait(true),
        };

        Assert.Equal(read, (await EditorApi.GetAsync(client, scenario.SiteA).ConfigureAwait(true)).StatusCode);

        var saved = await EditorApi.SaveAsync(client, scenario.SiteA, EditorApi.DefaultDocument, 0).ConfigureAwait(true);
        Assert.Equal(save, saved.StatusCode);
        if (save == HttpStatusCode.Forbidden)
        {
            Assert.Equal("identity.forbidden", (await PagesApi.ProblemAsync(saved).ConfigureAwait(true)).Code);
        }

        // The token usage count reads drafts, so it follows the page view capability.
        var usage = await EditorApi.TokenUsageAsync(client, scenario.SiteA, "color-brand").ConfigureAwait(true);
        Assert.Equal(read, usage.StatusCode);
    }
}
