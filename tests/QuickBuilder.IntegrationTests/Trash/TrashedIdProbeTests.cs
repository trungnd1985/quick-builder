using System.Net;
using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Every endpoint that names a page, component, item or file treats a trashed one exactly as one
/// that never existed (021 FR-006, FR-007, SC-004; research TR-03).
/// </summary>
/// <remarks>
/// The endpoints come from the running application, so a new one is covered without anyone
/// remembering it. Each is called twice, with the trashed id and with an unknown id, and must
/// give the same status and error code. The trash's own endpoints are the only exception.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed partial class TrashedIdProbeTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_endpoint_answers_for_a_trashed_id_as_for_one_that_never_existed()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "trash-probe");
        var (admin, site) = (scenario.Admin, scenario.Site);

        var page = (await PagesApi.SeedTreeAsync(admin, site, "probe-page"))["/probe-page"];
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, page, 1));

        var component = (await PagesApi.CreateComponentOkAsync(admin, site, "Probe")).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, component))).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, component, version));

        var item = await scenario.PostAsync("probe-item");
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, site, scenario.Posts, ItemsApi.Id(item), ItemsApi.Revision(item)));

        var file = await MediaApi.UploadOkAsync(admin, site);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, site, file.Id));

        var trashed = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["pageId"] = page,
            ["componentId"] = component,
            ["itemId"] = ItemsApi.Id(item),
            ["fileId"] = file.Id,
        };

        var endpoints = EndpointCatalog.Under(scenario.Cms.App, "/api")
            .Where(e => !e.Pattern.Contains("/trash", StringComparison.Ordinal)
                && e.Pattern.Contains("sites/{siteId", StringComparison.Ordinal)
                && trashed.Keys.Any(k => e.Pattern.Contains("{" + k, StringComparison.Ordinal)))
            .ToList();

        // A vacuous pass would look like coverage.
        Assert.True(endpoints.Count > 30, $"Only {endpoints.Count} endpoints were found.");

        var differences = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var (status, code) = await CallAsync(admin, endpoint.Method, Fill(endpoint.Pattern, site, scenario.Posts, trashed));
            var (unknownStatus, unknownCode) = await CallAsync(admin, endpoint.Method, Fill(endpoint.Pattern, site, scenario.Posts, trashed.ToDictionary(p => p.Key, _ => Guid.NewGuid(), StringComparer.Ordinal)));

            if (status != unknownStatus || !string.Equals(code, unknownCode, StringComparison.Ordinal))
            {
                differences.Add($"{endpoint.Method} {endpoint.Pattern}: {(int)status} {code}, unknown id: {(int)unknownStatus} {unknownCode}");
            }
        }

        Assert.True(differences.Count == 0, "These endpoints treat a trashed id differently from an unknown one:\n  " + string.Join("\n  ", differences));
    }

    private static async Task<(HttpStatusCode Status, string? Code)> CallAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method is not ("GET" or "HEAD" or "DELETE"))
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var match = ErrorCode().Match(body);
        return (response.StatusCode, match.Success ? match.Groups[1].Value : null);
    }

    /// <summary>The concrete path: the real site and collection, the given ids, plausible query values.</summary>
    private static string Fill(string pattern, Guid site, Guid collection, Dictionary<string, Guid> ids) =>
        "/" + Parameter().Replace(pattern.TrimStart('/'), match => match.Groups[1].Value switch
        {
            "siteId" => site.ToString(),
            "collectionId" => collection.ToString(),
            var name when ids.TryGetValue(name, out var id) => id.ToString(),
            "number" => "1",
            "scheduleAction" => "publish",
            _ => Guid.NewGuid().ToString(),
        }) + "?version=1&revision=1&expectedContentVersion=1";

    [GeneratedRegex(@"\{([A-Za-z]+)(?::[^}]*)?\}")]
    private static partial Regex Parameter();

    [GeneratedRegex(@"""errorCode""\s*:\s*""([^""]+)""")]
    private static partial Regex ErrorCode();
}
