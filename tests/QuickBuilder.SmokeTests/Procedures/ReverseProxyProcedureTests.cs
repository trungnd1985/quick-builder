using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests.Procedures;

/// <summary>
/// The Nginx, Caddy and Traefik pages and the sub-path variant, run as written beside a
/// documented install (026 T064; FR-018 – FR-020; SC-005; contracts/runnable-snippets.md §3).
/// </summary>
/// <remarks>
/// The only differences from what an operator runs are the closed substitutions: certificates
/// from a throwaway authority instead of an ACME client, and proxy ports on free loopback ports.
/// Each test proves that the proxy forwards host, scheme and client address and that
/// QuickBuilder believes it only because the page names the proxy:
/// <list type="bullet">
/// <item>two sites answer over HTTPS on their own host names;</item>
/// <item>setup and sign-in succeed with <c>Identity:AllowInsecureHttp</c> off, so the
/// forwarded <c>https</c> was trusted;</item>
/// <item>the sign-in security event records a client address that is not the proxy's;</item>
/// <item>the admin answers only on the admin host name;</item>
/// <item>a 5 MB upload passes the proxy's body limit;</item>
/// <item>under a base path, the admin shell and the scripts it names load through the proxy.</item>
/// </list>
/// </remarks>
[Trait("Category", "Procedures")]
public sealed partial class ReverseProxyProcedureTests(ITestOutputHelper output)
{
    private const string ProxyAddress = "172.30.0.10";

    [SkippableFact]
    [Trait("Procedure", "nginx")]
    public Task Nginx() => RunAsync("nginx", http => new Dictionary<string, string>
    {
        ["test-certificates"] =
            "/etc/letsencrypt/live/example.com/fullchain.pem>/certs/fullchain.pem;" +
            "/etc/letsencrypt/live/example.com/privkey.pem>/certs/privkey.pem;" +
            "/etc/letsencrypt:/etc/letsencrypt:ro>./certs:/certs:ro",
        ["ports"] = http,
    });

    [SkippableFact]
    [Trait("Procedure", "caddy")]
    public Task Caddy() => RunAsync("caddy", http => new Dictionary<string, string>
    {
        ["test-certificates"] =
            "\trequest_body {>\ttls /certs/fullchain.pem /certs/privkey.pem\\n\trequest_body {;" +
            "- caddy-config:/config>- caddy-config:/config\\n      - ./certs:/certs:ro",
        ["ports"] = http + ";\"443:443/udp\">\"127.0.0.1:{https}:443/udp\"",
    });

    [SkippableFact]
    [Trait("Procedure", "traefik")]
    public Task Traefik() => RunAsync("traefik", http => new Dictionary<string, string>
    {
        ["test-certificates"] =
            "--certificatesresolvers.le.acme.email=you@example.com>--providers.file.filename=/certs/tls.yml;" +
            "--certificatesresolvers.le.acme.storage=/letsencrypt/acme.json>--log.level=INFO;" +
            "--certificatesresolvers.le.acme.httpchallenge.entrypoint=web>--entrypoints.websecure.http.tls=true;" +
            "traefik-acme:/letsencrypt>./certs:/certs:ro;" +
            "tls.certresolver=le>tls=true",
        ["ports"] = http,
    });

    [SkippableFact]
    [Trait("Procedure", "proxy-basepath")]
    public async Task Sub_path_installation_with_nginx()
    {
        Skip.IfNot(await ProcedureRunner.AvailableAsync(), "Docker, bash or QB_SMOKE_IMAGE is not available.");
        await using var runner = new ProcedureRunner("basepath");
        using var authority = new TestCertificateAuthority();
        var https = ProcedureRunner.FreePort();

        await runner.RunProcedureAsync("install", runner.Root, new Dictionary<string, string> { ["release-compose"] = ProcedureRunner.ComposeFile });
        authority.WriteTo(Path.Combine(runner.InstallDirectory, "certs"));
        output.WriteLine(await runner.RunProcedureAsync("proxy-basepath", runner.InstallDirectory, new Dictionary<string, string>
        {
            ["test-certificates"] =
                "/etc/letsencrypt/live/example.com/fullchain.pem>/certs/fullchain.pem;" +
                "/etc/letsencrypt/live/example.com/privkey.pem>/certs/privkey.pem;" +
                "/etc/letsencrypt:/etc/letsencrypt:ro>./certs:/certs:ro",
            ["ports"] = Ports(ProcedureRunner.FreePort(), https),
        }));

        using var client = authority.ClientFor(https, "example.com");
        var api = new OperatorApi(client);
        await api.WaitForReadyAsync("/cms");
        await api.SetUpAsync(await runner.SetupCodeAsync(runner.InstallDirectory), "/cms");
        await api.CreatePublishedSiteAsync("Sub-path", "example.com", "Under a base path", "/cms");

        var (page, html) = await OperatorApi.PublicAsync(client, "example.com", "/cms/");
        Assert.Equal(HttpStatusCode.OK, page);
        Assert.Contains(">Under a base path</h1>", System.Text.Encoding.UTF8.GetString(html), StringComparison.Ordinal);

        var (robots, text) = await OperatorApi.PublicAsync(client, "example.com", "/robots.txt");
        Assert.Equal(HttpStatusCode.OK, robots);
        Assert.Contains("User-agent", System.Text.Encoding.UTF8.GetString(text), StringComparison.OrdinalIgnoreCase);

        await AssertAdminServedUnderAsync(client, "example.com", "/cms");
    }

    /// <summary>
    /// The admin shell, its deep links and its scripts through the proxy, under the base path
    /// (#51): the shell's asset addresses carry the base path, and each one it names loads.
    /// </summary>
    /// <remarks>
    /// The script itself derives the API's address from its own URL, which only a browser runs;
    /// that the URL it loads from is under the base path is what this can prove without one.
    /// </remarks>
    private async Task AssertAdminServedUnderAsync(HttpClient client, string hostName, string basePath)
    {
        var admin = $"{basePath}/admin/";
        foreach (var path in new[] { admin, $"{admin}sites" })
        {
            var (status, body) = await OperatorApi.PublicAsync(client, hostName, path);
            Assert.True(status == HttpStatusCode.OK, $"{path} answered {(int)status}.");
            var shell = System.Text.Encoding.UTF8.GetString(body);
            Assert.DoesNotContain("=\"./", shell, StringComparison.Ordinal);

            var assets = AdminAsset().Matches(shell).Select(m => m.Groups["path"].Value).Distinct().ToList();
            Assert.True(assets.Count > 0, $"{path} names no script or stylesheet:\n{shell}");
            foreach (var asset in assets)
            {
                Assert.True(asset.StartsWith(admin, StringComparison.Ordinal), $"{path} names {asset}, which is not under {admin}.");
                var (assetStatus, _) = await OperatorApi.PublicAsync(client, hostName, asset);
                Assert.True(assetStatus == HttpStatusCode.OK, $"{asset}, named by {path}, answered {(int)assetStatus}.");
            }

            output.WriteLine($"{path}: shell and {assets.Count} asset(s) served under {basePath}");
        }
    }

    private static string Ports(int http, int https) =>
        $"\"80:80\">\"127.0.0.1:{http.ToString(CultureInfo.InvariantCulture)}:80\";\"443:443\">\"127.0.0.1:{https.ToString(CultureInfo.InvariantCulture)}:443\"";

    private async Task RunAsync(string procedure, Func<string, Dictionary<string, string>> substitutions)
    {
        Skip.IfNot(await ProcedureRunner.AvailableAsync(), "Docker, bash or QB_SMOKE_IMAGE is not available.");
        await using var runner = new ProcedureRunner(procedure);
        using var authority = new TestCertificateAuthority();
        var https = ProcedureRunner.FreePort();
        var values = substitutions(Ports(ProcedureRunner.FreePort(), https));
        values["ports"] = values["ports"].Replace("{https}", https.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        await runner.RunProcedureAsync("install", runner.Root, new Dictionary<string, string> { ["release-compose"] = ProcedureRunner.ComposeFile });
        authority.WriteTo(Path.Combine(runner.InstallDirectory, "certs"));
        output.WriteLine(await runner.RunProcedureAsync(procedure, runner.InstallDirectory, values));

        // Claimed over HTTPS through the proxy: refused unless the forwarded scheme was trusted.
        using var admin = authority.ClientFor(https, "admin.example.com");
        var api = new OperatorApi(admin);
        await api.WaitForReadyAsync();
        await api.SetUpAsync(await runner.SetupCodeAsync(runner.InstallDirectory));
        await api.SignInAsync();

        var firstSite = await api.CreatePublishedSiteAsync("Example", "example.com", "Example over HTTPS");
        await api.CreatePublishedSiteAsync("Shop", "shop.example.net", "Shop over HTTPS");

        using var example = authority.ClientFor(https, "example.com");
        using var shop = authority.ClientFor(https, "shop.example.net");
        var (exampleStatus, exampleBody) = await OperatorApi.PublicAsync(example, "example.com", "/");
        var (shopStatus, shopBody) = await OperatorApi.PublicAsync(shop, "shop.example.net", "/");
        Assert.Equal(HttpStatusCode.OK, exampleStatus);
        Assert.Contains(">Example over HTTPS</h1>", System.Text.Encoding.UTF8.GetString(exampleBody), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, shopStatus);
        Assert.Contains(">Shop over HTTPS</h1>", System.Text.Encoding.UTF8.GetString(shopBody), StringComparison.Ordinal);

        // The admin only on its own host name (Hosting:AdminHostNames).
        var (adminOnSite, _) = await OperatorApi.PublicAsync(example, "example.com", "/api/v1/identity/status");
        Assert.Equal(HttpStatusCode.NotFound, adminOnSite);

        // The client's address, not the proxy's, on the sign-in event.
        var events = await runner.ComposeAsync(
            runner.InstallDirectory,
            "exec", "-T", "db", "psql", "-U", "quickbuilder", "-d", "quickbuilder", "-At", "-c",
            "SELECT host(client_address) FROM identity.security_events WHERE event_type = 'sign_in' ORDER BY occurred_at_utc DESC LIMIT 1");
        var address = events.Output.Trim();
        output.WriteLine($"sign-in recorded from {address}");
        Assert.True(events.ExitCode == 0 && address.Length > 0, events.Output);
        Assert.NotEqual(ProxyAddress, address);

        // A body larger than any proxy's default limit reaches QuickBuilder.
        await api.UploadAsync(firstSite.SiteId, OperatorApi.Pdf(5), "application/pdf", "large.pdf");
    }

    // A script or stylesheet address in the admin shell, wherever it points.
    [GeneratedRegex(@"(?:src|href)=""(?<path>[^""]+\.(?:js|css))""", RegexOptions.CultureInvariant)]
    private static partial Regex AdminAsset();
}
