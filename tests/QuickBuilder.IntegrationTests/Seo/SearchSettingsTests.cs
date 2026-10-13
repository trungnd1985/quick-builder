using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// A site's search settings: title suffix, default sharing image, organisation (017 FR-006,
/// FR-021; research SE-03, SE-08; seo-api.yaml).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SearchSettingsTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "seo-search-settings").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_settings_are_saved_and_returned_with_who_changed_them()
    {
        var image = Guid.NewGuid();
        var logo = Guid.NewGuid();

        var site = await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, " | Acme", image, "Acme Ltd", logo).ConfigureAwait(true);

        var search = site.GetProperty("search");
        Assert.Equal(" | Acme", search.GetProperty("titleSuffix").GetString());
        Assert.Equal(image, search.GetProperty("defaultImageId").GetGuid());
        Assert.Equal("Acme Ltd", search.GetProperty("organisationName").GetString());
        Assert.Equal(logo, search.GetProperty("organisationLogoId").GetGuid());
        Assert.NotEqual(JsonValueKindNull, search.GetProperty("updatedBy").ValueKind);

        var visibility = site.GetProperty("searchVisibility");
        Assert.False(visibility.GetProperty("discourageSearchEngines").GetBoolean());
        Assert.True(visibility.GetProperty("redirectOtherHostNames").GetBoolean());
    }

    [Fact]
    public async Task Empty_values_clear_the_settings()
    {
        await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, " | Acme", organisationName: "Acme").ConfigureAwait(true);

        var site = await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, "  ", organisationName: "").ConfigureAwait(true);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, site.GetProperty("search").GetProperty("titleSuffix").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, site.GetProperty("search").GetProperty("organisationName").ValueKind);
    }

    [Theory]
    [InlineData("x", 61, null)]
    [InlineData("line\nbreak", 1, null)]
    [InlineData(null, 0, "o")]
    public async Task Values_out_of_bounds_are_refused(string? suffixPart, int repeat, string? organisationPart)
    {
        var suffix = suffixPart is null ? null : string.Concat(Enumerable.Repeat(suffixPart, repeat));
        var organisation = organisationPart is null ? null : new string('o', 201);
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var response = await SeoApi.PutSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, version, suffix, organisationName: organisation).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "sites.search_invalid").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, " | One").ConfigureAwait(true);

        var response = await SeoApi.PutSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, version, " | Two").ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.Conflict, "sites.out_of_date").ConfigureAwait(true);
    }

    [Fact]
    public async Task Designers_may_change_them_and_editors_may_not()
    {
        var designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);
        var editor = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(true);
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var refused = await SeoApi.PutSearchSettingsAsync(editor, _scenario.SiteA, version, " | E").ConfigureAwait(true);
        await PagesApi.RefusedAsync(refused, HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(true);

        var allowed = await SeoApi.PutSearchSettingsAsync(designer, _scenario.SiteA, version, " | D").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_site_gets_not_found()
    {
        var outsider = await _scenario.UserAsync((_scenario.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        var response = await SeoApi.PutSearchSettingsAsync(outsider, _scenario.SiteA, 1, " | X").ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.NotFound, "sites.not_found").ConfigureAwait(true);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;
}
