using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QuickBuilder.SmokeTests.Procedures;

/// <summary>
/// The admin calls the procedure tests need after an operator finishes a page: claim the
/// installation, create sites, publish a page, upload a file, and read the public site
/// (the same API the reference journey drives).
/// </summary>
internal sealed class OperatorApi(HttpClient client)
{
    public const string Email = "operator@procedures.test";
    public const string Password = "procedure test passphrase 2026";

    public HttpClient Client { get; } = client;

    /// <summary>Waits until <c>/health/ready</c> answers 200.</summary>
    public async Task WaitForReadyAsync(string basePath = "")
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var ready = await Client.GetAsync(new Uri($"{basePath}/health/ready", UriKind.Relative)).ConfigureAwait(false);
                if (ready.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The installation never reported ready.");
    }

    /// <summary>Claims the installation with the setup code and keeps the session.</summary>
    public async Task SetUpAsync(string setupCode, string basePath = "")
    {
        Client.DefaultRequestHeaders.Remove("X-QuickBuilder-Request");
        Client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        using var setup = await Client
            .PostAsJsonAsync(
                new Uri($"{basePath}/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Procedure Operator", email = Email, password = Password })
            .ConfigureAwait(false);
        Assert.True(setup.StatusCode == HttpStatusCode.Created, $"Setup failed ({(int)setup.StatusCode}): {await setup.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        KeepSession(setup);
    }

    /// <summary>Signs in again, after a restore, as the operator the backup knew.</summary>
    public async Task SignInAsync(string basePath = "")
    {
        Client.DefaultRequestHeaders.Remove("X-QuickBuilder-Request");
        Client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        using var signIn = await Client
            .PostAsJsonAsync(new Uri($"{basePath}/api/v1/identity/sign-in", UriKind.Relative), new { email = Email, password = Password })
            .ConfigureAwait(false);
        Assert.True(signIn.StatusCode == HttpStatusCode.OK, $"Sign-in failed ({(int)signIn.StatusCode}): {await signIn.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        KeepSession(signIn);
    }

    /// <summary>Creates a site holding <paramref name="hostName"/>, with a published home page showing <paramref name="heading"/>.</summary>
    public async Task<PublishedSite> CreatePublishedSiteAsync(string name, string hostName, string heading, string basePath = "")
    {
        using var created = await Client
            .PostAsJsonAsync(new Uri($"{basePath}/api/v1/sites", UriKind.Relative), new { name, hostNames = new[] { hostName } })
            .ConfigureAwait(false);
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"Creating {hostName} failed: {await created.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        var siteId = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();

        var pages = $"{basePath}/api/v1/pages/sites/{siteId}/pages";
        using var home = await Client.PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Home", slug = "home", isHome = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, home.StatusCode);
        var homePath = $"{pages}/{(await home.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid()}";

        var version = await SaveDraftAsync(homePath, Document(heading)).ConfigureAwait(false);
        using var published = await Client.PostAsJsonAsync(new Uri($"{homePath}/publish", UriKind.Relative), new { draftVersion = version }).ConfigureAwait(false);
        Assert.True(published.StatusCode == HttpStatusCode.OK, $"Publishing failed: {await published.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        return new PublishedSite(siteId, homePath);
    }

    /// <summary>Saves a draft of the home page that is not published, so a restore can be checked for drafts.</summary>
    public async Task SaveHomeDraftAsync(PublishedSite site, string heading) =>
        await SaveDraftAsync(site.HomePath, Document(heading)).ConfigureAwait(false);

    /// <summary>The home page draft's document, as the admin reads it.</summary>
    public async Task<string> HomeDraftAsync(PublishedSite site)
    {
        var draft = await Client.GetFromJsonAsync<JsonElement>(new Uri($"{site.HomePath}/draft", UriKind.Relative)).ConfigureAwait(false);
        return draft.GetProperty("document").GetRawText();
    }

    /// <summary>Uploads a file to a site's library and returns its public path.</summary>
    public async Task<string> UploadAsync(Guid siteId, byte[] bytes, string contentType, string fileName, string basePath = "")
    {
        using var upload = new HttpRequestMessage(HttpMethod.Post, new Uri($"{basePath}/api/v1/media/sites/{siteId}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        upload.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        upload.Headers.Add("X-QuickBuilder-File-Name", fileName);
        using var uploaded = await Client.SendAsync(upload).ConfigureAwait(false);
        Assert.True(uploaded.StatusCode == HttpStatusCode.Created, $"Upload failed ({(int)uploaded.StatusCode}): {await uploaded.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        return (await uploaded.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("publicPath").GetString()!;
    }

    /// <summary>A public page or file as a visitor of <paramref name="hostName"/> gets it.</summary>
    public static async Task<(HttpStatusCode Status, byte[] Body)> PublicAsync(HttpClient client, string hostName, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = hostName;
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
    }

    /// <summary>A small, valid PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>A PDF of about <paramref name="megabytes"/> MB, larger than any proxy's default body limit.</summary>
    public static byte[] Pdf(int megabytes)
    {
        var header = "%PDF-1.4\n1 0 obj <<>> endobj\ntrailer <<>>\n%"u8.ToArray();
        var bytes = new byte[megabytes * 1024 * 1024];
        header.CopyTo(bytes, 0);
        for (var i = header.Length; i < bytes.Length - 6; i++)
        {
            bytes[i] = (byte)'a';
        }

        "\n%%EOF"u8.ToArray().CopyTo(bytes, bytes.Length - 6);
        return bytes;
    }

    private static string Document(string heading) => $$$"""
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"s1","type":"section","props":{},"children":[
            {"id":"h1","type":"heading","props":{"text":"{{{heading}}}","level":1},"children":[]}]}]}}
        """;

    private async Task<uint> SaveDraftAsync(string subject, string document)
    {
        var draft = new Uri($"{subject}/draft", UriKind.Relative);
        var version = (await Client.GetFromJsonAsync<JsonElement>(draft).ConfigureAwait(false)).GetProperty("version").GetUInt32();
        using var body = new StringContent($$"""{"version":{{version}},"document":{{document}}}""", System.Text.Encoding.UTF8, "application/json");
        using var saved = await Client.PutAsync(draft, body).ConfigureAwait(false);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"Saving a draft failed: {await saved.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        return (await saved.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("version").GetUInt32();
    }

    private void KeepSession(HttpResponseMessage response)
    {
        var session = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0];
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.Add("Cookie", session);
    }
}

/// <summary>A site with a published home page, and where that page lives in the admin API.</summary>
internal sealed record PublishedSite(Guid SiteId, string HomePath);
