using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Sites;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>Requests and documents for the 017 tests (contracts/seo-api.yaml).</summary>
internal static class SeoApi
{
    public static string SearchSettingsPath(Guid siteId) => $"/api/v1/sites/{siteId}/search-settings";

    public static string SearchVisibilityPath(Guid siteId) => $"/api/v1/sites/{siteId}/search-visibility";

    /// <summary>The site's current version, for a settings change (009 S-06).</summary>
    public static async Task<uint> SiteVersionAsync(HttpClient client, Guid siteId)
    {
        var site = await PagesApi.OkAsync(await SitesApi.GetAsync(client, siteId).ConfigureAwait(false)).ConfigureAwait(false);
        return site.GetProperty("version").GetUInt32();
    }

    public static Task<HttpResponseMessage> PutSearchSettingsAsync(
        HttpClient client, Guid siteId, uint version, string? titleSuffix = null, Guid? defaultImageId = null, string? organisationName = null, Guid? organisationLogoId = null) =>
        client.PutAsJsonAsync(
            new Uri(SearchSettingsPath(siteId), UriKind.Relative),
            new { titleSuffix, defaultImageId, organisationName, organisationLogoId, version });

    /// <summary>Sets the search settings as the administrator, reading the version first.</summary>
    public static async Task<JsonElement> SetSearchSettingsAsync(
        HttpClient admin, Guid siteId, string? titleSuffix = null, Guid? defaultImageId = null, string? organisationName = null, Guid? organisationLogoId = null)
    {
        var version = await SiteVersionAsync(admin, siteId).ConfigureAwait(false);
        return await PagesApi.OkAsync(
            await PutSearchSettingsAsync(admin, siteId, version, titleSuffix, defaultImageId, organisationName, organisationLogoId).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    public static Task<HttpResponseMessage> PutSearchVisibilityAsync(
        HttpClient client, Guid siteId, uint version, bool discourage = false, string? robotsRules = null, bool redirectOtherHostNames = false) =>
        client.PutAsJsonAsync(
            new Uri(SearchVisibilityPath(siteId), UriKind.Relative),
            new { discourageSearchEngines = discourage, robotsRules, redirectOtherHostNames, version });

    /// <summary>Sets the search visibility as the administrator, reading the version first.</summary>
    public static async Task<JsonElement> SetSearchVisibilityAsync(
        HttpClient admin, Guid siteId, bool discourage = false, string? robotsRules = null, bool redirectOtherHostNames = false)
    {
        var version = await SiteVersionAsync(admin, siteId).ConfigureAwait(false);
        return await PagesApi.OkAsync(
            await PutSearchVisibilityAsync(admin, siteId, version, discourage, robotsRules, redirectOtherHostNames).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A page document with a heading, whose root carries <paramref name="seo"/> (a JSON object),
    /// stamped with the binding version when it binds metadata.
    /// </summary>
    public static string DocumentWithSeo(string heading, string seo)
    {
        var document = JsonNode.Parse(Publishing.PublishingApi.HeadingDocument(heading))!.AsObject();
        var seoNode = JsonNode.Parse(seo)!.AsObject();
        if (seoNode.ContainsKey("bind") || seoNode["structuredData"]?["bind"] is not null)
        {
            document["bindingVersion"] = 1;
        }

        document["root"]!["props"] = new JsonObject { ["seo"] = seoNode };
        return document.ToJsonString();
    }

    /// <summary>The value of the first <c>&lt;meta&gt;</c> or <c>&lt;link&gt;</c> with this attribute, decoded.</summary>
    public static string? Head(string html, string attribute, string name, string valueAttribute = "content")
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            $"<(?:meta|link) {attribute}=\"{System.Text.RegularExpressions.Regex.Escape(name)}\" {valueAttribute}=\"([^\"]*)\">");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    public static string? Title(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "<title>([^<]*)</title>");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }
}
