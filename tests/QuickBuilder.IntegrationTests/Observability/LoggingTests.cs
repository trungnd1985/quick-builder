using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// One identifier follows a request end to end, and nothing sensitive travels with it
/// (SC-008, FR-031, FR-031a, FR-032).
/// </summary>
/// <remarks>
/// SC-008 asks for two things that pull in opposite directions: enough context to trace
/// one request through the logs, and none of the content that makes logs a liability.
/// Both are tested here because satisfying either alone is easy.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class LoggingTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("logging").ConfigureAwait(true);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// FR-031a: the identifier returned to a caller is the trace identifier.
    /// </summary>
    /// <remarks>
    /// This is the whole of SC-008's "trace one request by one identifier". An error
    /// response carries a <c>traceId</c>; the log entries for that request carry the same
    /// value because both read it from the current activity. If the response invented its
    /// own identifier, an operator holding it could find nothing.
    /// <para>
    /// Asserted by supplying a W3C <c>traceparent</c> and checking the response echoes
    /// that trace id back — which only holds if the correlation identifier really is the
    /// trace, not a parallel scheme.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Error_responses_carry_the_trace_id_of_the_request()
    {
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var spanId = ActivitySpanId.CreateRandom().ToString();

        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{spanId}-01");

        // A validation failure is the cheapest way to get a problem-details body back.
        var response = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "", hostNames = Array.Empty<string>() })
            .ConfigureAwait(true);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.True(problem.TryGetProperty("traceId", out var returned));
        Assert.Equal(traceId, returned.GetString());
    }

    /// <summary>
    /// FR-032: a rejected request does not echo secrets back, and nothing logs the payload.
    /// </summary>
    /// <remarks>
    /// The realistic leak is not a deliberate log of a password — it is a validation error
    /// helpfully quoting the offending value, or an exception message carrying the request
    /// body. Both would put a secret somewhere it is retained and shipped to a log
    /// collector.
    /// <para>
    /// A site name is not itself sensitive; it stands in for any user-supplied value, and
    /// the assertion is that a marker string submitted in a request does not come back in
    /// the response body.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Rejected_requests_do_not_echo_the_submitted_value()
    {
        const string secretish = "hunter2-correct-horse-battery-staple";

        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        var response = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                // The name is fine; the host name is not, so this fails validation while
                // carrying the marker in a field that error messages might quote.
                new { name = secretish, hostNames = new[] { "not a valid host" } })
            .ConfigureAwait(true);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.DoesNotContain(secretish, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unexpected failure reveals nothing internal to the caller (FR-034).
    /// </summary>
    /// <remarks>
    /// Checks the negative: no stack frame, no type name, no connection string fragment.
    /// The caller gets a trace id and nothing else, which is the only detail that helps
    /// them and the only one safe to give.
    /// </remarks>
    [Fact]
    public async Task Unknown_routes_do_not_leak_internals()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        var response = await client
            .GetAsync(new Uri("/api/v1/sites/not-a-guid", UriKind.Relative))
            .ConfigureAwait(true);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        foreach (var leak in new[] { "QuickBuilder.Sites.Infrastructure", "Npgsql", "Password=", "at QuickBuilder" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 003 SC-012, FR-030: no media journey writes a file's name to the logs.
    /// </summary>
    /// <remarks>
    /// Every name uploaded here carries a canary marker. Upload (accepted and refused), list,
    /// preview, public serving, a miss, deletion and the orphan sweep all run; then every
    /// captured event — message, template, properties, exception — is searched for the marker.
    /// A file's name can itself be personal data ("jane-doe-passport.pdf"), which is why the
    /// logs carry ids only.
    /// </remarks>
    [Fact]
    public async Task Media_journeys_never_log_a_file_name()
    {
        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await Media.MediaApi.CreateSiteAsync(admin, "Logged", "logged-media.test").ConfigureAwait(true);

        var accepted = await Media.MediaApi.UploadOkAsync(admin, siteId).ConfigureAwait(true);
        var refused = await Media.MediaApi
            .UploadAsync(admin, siteId, "<script>"u8.ToArray(), Media.MediaApi.CanaryName("png"), "image/png")
            .ConfigureAwait(true);
        Assert.False(refused.IsSuccessStatusCode);

        await Media.MediaApi.ListOkAsync(admin, siteId).ConfigureAwait(true);
        await Media.MediaApi.PreviewAsync(admin, siteId, accepted.Id).ConfigureAwait(true);
        await Media.MediaApi.PublicAsync(_app, "logged-media.test", accepted.PublicPath).ConfigureAwait(true);
        await Media.MediaApi.PublicAsync(_app, "logged-media.test", Media.MediaApi.PublicPath(Guid.NewGuid(), "png")).ConfigureAwait(true);
        await Media.MediaApi.DeleteAsync(admin, siteId, accepted.Id).ConfigureAwait(true);
        await _app.Services.GetRequiredService<QuickBuilder.Media.Infrastructure.MediaOrphanSweepService>()
            .SweepAsync(CancellationToken.None).ConfigureAwait(true);

        var events = _app.Logs.Events.Select(LogCapture.Render).ToList();
        Assert.NotEmpty(events);

        var leak = events.FirstOrDefault(e => e.Contains(Media.MediaApi.CanaryMarker, StringComparison.OrdinalIgnoreCase));
        Assert.True(leak is null, $"A log event contains an uploaded file's name: {leak}");

        // And the events that should be there, are — by id.
        Assert.Contains(events, e => e.Contains(accepted.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 010 FR-037: pages and components are logged by id, never by title, name or content.
    /// </summary>
    /// <remarks>
    /// Titles, component names and a node id carry the canary marker; the journey covers
    /// create, rename, a refused change, draft saves (accepted and refused) and both deletes.
    /// </remarks>
    [Fact]
    public async Task Page_journeys_never_log_titles_names_or_content()
    {
        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await Media.MediaApi.CreateSiteAsync(admin, "Logged", "logged-pages.test").ConfigureAwait(true);
        var canary = Pages.PagesApi.CanaryMarker;

        var page = (await Pages.PagesApi.CreateOkAsync(admin, siteId, "about", title: $"{canary}title").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await Pages.PagesApi.UpdateAsync(admin, siteId, page, $"{canary}renamed", "about", 1).ConfigureAwait(true);
        await Pages.PagesApi.CreateAsync(admin, siteId, "about", title: $"{canary}clash").ConfigureAwait(true);
        var component = (await Pages.PagesApi.CreateComponentOkAsync(admin, siteId, $"{canary}name").ConfigureAwait(true)).GetProperty("id").GetGuid();

        var path = Pages.PagesApi.PagePath(siteId, page);
        await Pages.PagesApi.SaveCurrentDraftAsync(admin, path, $$$"""{"schemaVersion":1,"root":{"id":"{{{canary}}}root","type":"root","props":{},"children":[]}}""").ConfigureAwait(true);
        await Pages.PagesApi.SaveCurrentDraftAsync(admin, path, $$$"""{"schemaVersion":1,"root":{"id":"root","type":"{{{canary}}}type","props":{},"children":[]}}""").ConfigureAwait(true);

        var version = (await Pages.PagesApi.OkAsync(await Pages.PagesApi.GetComponentAsync(admin, siteId, component).ConfigureAwait(true)).ConfigureAwait(true))
            .GetProperty("version").GetUInt32();
        await Pages.PagesApi.DeleteComponentAsync(admin, siteId, component, version).ConfigureAwait(true);
        await Pages.PagesApi.DeleteAsync(admin, siteId, page, 2).ConfigureAwait(true);

        var events = _app.Logs.Events.Select(LogCapture.Render).ToList();
        var leak = events.FirstOrDefault(e => e.Contains(canary, StringComparison.OrdinalIgnoreCase));
        Assert.True(leak is null, $"A log event contains page content: {leak}");

        Assert.Contains(events, e => e.Contains(page.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(events, e => e.Contains(component.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 011 FR-045: block text saved in a draft, and design token names and values, never reach
    /// the logs; the token set is logged by site id and version only.
    /// </summary>
    [Fact]
    public async Task Editor_journeys_never_log_block_text_or_token_names_and_values()
    {
        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var siteId = await Media.MediaApi.CreateSiteAsync(admin, "Logged", "logged-editor.test").ConfigureAwait(true);
        const string Canary = "zz-editor-canary";

        var page = (await Pages.PagesApi.CreateOkAsync(admin, siteId, "home", isHome: true).ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = Pages.PagesApi.PagePath(siteId, page);
        var document = $$$"""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[
                {"id":"h","type":"heading","props":{"text":"{{{Canary}}} heading"},"children":[]},
                {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{{Canary}}} rich"}]}]}},"children":[]}]}]}}
            """;
        await Pages.PagesApi.OkAsync(await Pages.PagesApi.SaveCurrentDraftAsync(admin, path, document).ConfigureAwait(true)).ConfigureAwait(true);
        // A refused one too: the refusal must not quote it either.
        await Pages.PagesApi.SaveCurrentDraftAsync(admin, path, document.Replace("\"heading\",\"props\":{", "\"heading\",\"props\":{\"level\":9,", StringComparison.Ordinal)).ConfigureAwait(true);

        var tokens = Editor.EditorApi.DefaultWithColor("color-canary", $"{Canary} name", "#123abc");
        await Pages.PagesApi.OkAsync(await Editor.EditorApi.SaveAsync(admin, siteId, tokens, 0).ConfigureAwait(true)).ConfigureAwait(true);
        await Editor.EditorApi.SaveAsync(admin, siteId, tokens.Replace("#123abc", "not-a-colour", StringComparison.Ordinal), 1).ConfigureAwait(true);
        await Editor.EditorApi.TokenUsageAsync(admin, siteId, "color-canary").ConfigureAwait(true);

        var events = _app.Logs.Events.Select(LogCapture.Render).ToList();
        var leak = events.FirstOrDefault(e =>
            e.Contains(Canary, StringComparison.OrdinalIgnoreCase) || e.Contains("#123abc", StringComparison.OrdinalIgnoreCase));
        Assert.True(leak is null, $"A log event contains editor content: {leak}");

        Assert.Contains(events, e => e.Contains("Design tokens", StringComparison.Ordinal) && e.Contains(siteId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 009 FR-032: requested host names never reach the logs — not for unknown hosts, not
    /// for disabled sites, not for admin paths turned away from a client domain, not for
    /// forwarded headers. A probe's host name is exactly what someone mapping an
    /// installation would want confirmed.
    /// </summary>
    [Fact]
    public async Task Site_resolution_never_logs_a_requested_host_name()
    {
        const string Canary = "zz-host-canary";
        using var app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("logging-hosts").ConfigureAwait(true),
            settings: new Dictionary<string, string>
            {
                ["Hosting:AdminHostNames:0"] = "admin.test",
                ["Hosting:KnownProxies:0"] = "10.0.0.1",
            });
        var admin = await app.CreateAdminClientAsync("admin.test").ConfigureAwait(true);
        var disabled = (await Sites.SitesApi.CreateOkAsync(admin, "Quiet", [$"{Canary}-disabled.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();
        await Sites.SitesApi.CreateOkAsync(admin, "Client", [$"{Canary}-client.test"]).ConfigureAwait(true);
        await Sites.SitesApi.DisableAsync(admin, disabled).ConfigureAwait(true);

        await Sites.SitesApi.PublicGetAsync(app, $"{Canary}-unknown.test").ConfigureAwait(true);
        await Sites.SitesApi.PublicGetAsync(app, $"{Canary}-disabled.test").ConfigureAwait(true);
        await Sites.SitesApi.PublicGetAsync(app, $"{Canary}-client.test", "/admin/").ConfigureAwait(true);
        await Sites.SitesApi.PublicGetAsync(app, $"{Canary}-client.test", "/api/v1/sites").ConfigureAwait(true);
        await Sites.SitesApi.PublicGetAsync(app, $"{Canary}-client.test", "/no-such-page").ConfigureAwait(true);

        using var forwarded = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        forwarded.Headers.Host = "internal.local";
        forwarded.Headers.Add(IdentitySeed.RemoteAddressHeader, "10.0.0.1");
        forwarded.Headers.Add("X-Forwarded-Host", $"{Canary}-forwarded.test");
        forwarded.Headers.Add("X-Forwarded-For", "203.0.113.1");
        await app.CreateAnonymousClient().SendAsync(forwarded).ConfigureAwait(true);

        var events = app.Logs.Events.Select(LogCapture.Render).ToList();
        Assert.NotEmpty(events);

        var leak = events.FirstOrDefault(e => e.Contains(Canary, StringComparison.OrdinalIgnoreCase));
        Assert.True(leak is null, $"A log event contains a requested host name: {leak}");
    }

    /// <summary>
    /// 002 SC-009, FR-030: the identity journeys leave no personal data or secret in the logs.
    /// </summary>
    /// <remarks>
    /// Drives every flow that handles an email address, a name, a password or a token, then
    /// searches everything captured — messages, templates, properties and exceptions — for
    /// each of them. The setup-code banner is the one event allowed to carry a secret (FR-004a),
    /// so it is set aside, and the code is asserted to appear nowhere else.
    /// </remarks>
    [Fact]
    public async Task Identity_journeys_leave_no_personal_data_or_secrets_in_the_logs()
    {
        const string ownerEmail = "logged.owner@example.test";
        const string ownerName = "Quillon Ravensworth";
        const string ownerPassword = "owner-marker-passphrase-4471";
        const string wrongPassword = "wrong-marker-passphrase-9902";
        const string newEmail = "logged.newcomer@example.test";
        const string changedEmail = "logged.renamed@example.test";
        const string newPassword = "newcomer-marker-passphrase-3318";

        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);
        var setup = await SetupHelpers
            .PostSetupAsync(SetupHelpers.ClientFrom(_app, "198.51.100.20"), code, ownerEmail, ownerPassword, ownerName)
            .ConfigureAwait(true);
        setup.EnsureSuccessStatusCode();

        var failed = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "198.51.100.21"), ownerEmail, wrongPassword).ConfigureAwait(true);
        Assert.False(failed.IsSuccessStatusCode);
        var signedIn = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "198.51.100.21"), ownerEmail, ownerPassword).ConfigureAwait(true);
        signedIn.EnsureSuccessStatusCode();

        var admin = SignInHelpers.Browser(_app, "198.51.100.21", SignInHelpers.Cookie(signedIn, "qb_session"));
        var (userId, firstToken) = await UsersApi.CreateUserAsync(admin, newEmail).ConfigureAwait(true);
        (await UsersApi.UpdateAsync(admin, userId, new { email = changedEmail }).ConfigureAwait(true)).EnsureSuccessStatusCode();
        var secondToken = await UsersApi.IssueLinkTokenAsync(admin, userId).ConfigureAwait(true);
        (await UsersApi.SetPasswordAsync(SignInHelpers.Browser(_app, "198.51.100.22"), secondToken, newPassword).ConfigureAwait(true)).EnsureSuccessStatusCode();

        await using var scope = _app.Services.CreateAsyncScope();
        var sessionIds = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Sessions.Select(s => s.Id).ToListAsync().ConfigureAwait(true);
        Assert.NotEmpty(sessionIds);

        string[] forbidden =
        [
            ownerEmail, ownerName, ownerPassword, wrongPassword, newEmail, changedEmail, "New Person", newPassword,
            firstToken, secondToken,
            SetupHelpers.CookieValue(setup, "qb_session")!, SetupHelpers.CookieValue(signedIn, "qb_session")!,
            .. sessionIds.SelectMany(id => new[] { id.ToString("D"), id.ToString("N") }),
        ];

        var events = _app.Logs.Events.Where(e => !e.Properties.ContainsKey("SetupCode")).Select(LogCapture.Render).ToList();
        Assert.NotEmpty(events);

        foreach (var secret in forbidden.Append(code).Append($"{code[..4]}-{code[4..8]}-{code[8..]}"))
        {
            var leak = events.FirstOrDefault(e => e.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.True(leak is null, $"A log event contains a value that must never be logged: {leak}");
        }
    }
}
