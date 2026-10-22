using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// The world Forms tests start from (018): a claimed installation, an administrator, sites A and
/// B on <see cref="CmsScenario.HostA"/> and <see cref="CmsScenario.HostB"/>, and helpers to
/// publish pages and components with forms, deliver events and read the Forms tables.
/// </summary>
internal sealed class FormsWorld : IDisposable
{
    private FormsWorld(CmsScenario scenario)
    {
        Scenario = scenario;
    }

    public CmsScenario Scenario { get; }

    public QuickBuilder.IntegrationTests.Infrastructure.QuickBuilderApp App => Scenario.App;

    public HttpClient Admin => Scenario.Admin;

    public Guid Site => Scenario.SiteA;

    public Guid SiteB => Scenario.SiteB;

    public static async Task<FormsWorld> CreateAsync(
        Infrastructure.DatabaseFixture database, string label, Action<IServiceCollection>? configureTestServices = null, IReadOnlyDictionary<string, string>? settings = null) =>
        new(await CmsScenario.CreateAsync(database, label, configureTestServices, WithDefaults(settings)).ConfigureAwait(false));

    /// <summary>
    /// Every request in a test comes from one address, so the rate limit would stop any test
    /// that sends more than five submissions. Limits are high unless a test sets its own.
    /// </summary>
    private static Dictionary<string, string> WithDefaults(IReadOnlyDictionary<string, string>? settings)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Forms:RateLimit:PerMinute"] = "1000",
            ["Forms:RateLimit:PerHour"] = "10000",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            merged[key] = value;
        }

        return merged;
    }

    /// <summary>A signed-in user holding the given site roles.</summary>
    public Task<HttpClient> UserAsync(params (Guid SiteId, SystemRole Role)[] roles) => Scenario.UserAsync(roles);

    /// <summary>A new page of site A (or <paramref name="site"/>) under the root.</summary>
    public async Task<Guid> PageAsync(string slug, Guid? site = null) =>
        (await PagesApi.CreateOkAsync(Admin, site ?? Site, slug).ConfigureAwait(false)).GetProperty("id").GetGuid();

    /// <summary>A new component of site A (or <paramref name="site"/>).</summary>
    public async Task<Guid> ComponentAsync(string name, Guid? site = null) =>
        (await PagesApi.CreateComponentOkAsync(Admin, site ?? Site, name).ConfigureAwait(false)).GetProperty("id").GetGuid();

    /// <summary>Saves <paramref name="document"/> as a page's draft and publishes it.</summary>
    public Task PublishPageAsync(Guid pageId, string document, Guid? site = null) =>
        PublishAsync(PagesApi.PagePath(site ?? Site, pageId), document);

    /// <summary>Saves <paramref name="document"/> as a component's draft and publishes it.</summary>
    public Task PublishComponentAsync(Guid componentId, string document, Guid? site = null) =>
        PublishAsync(PagesApi.ComponentPath(site ?? Site, componentId), document);

    /// <summary>Delivers every pending event, as the dispatcher would, until none is left.</summary>
    public async Task DeliverEventsAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(App.Services);
        var settings = App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    /// <summary>Runs <paramref name="work"/> against the Forms context.</summary>
    public async Task<T> WithFormsAsync<T>(Func<FormsDbContext, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = App.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<FormsDbContext>()).ConfigureAwait(false);
    }

    /// <summary>The registry rows of a site: form id, name and retention, in id order.</summary>
    public Task<List<(string FormId, string Name, int? RetentionDays)>> FormsAsync(Guid? site = null) =>
        WithFormsAsync(async database => (await database.ForSite<QuickBuilder.Forms.Domain.FormRecord>(site ?? Site)
                .AsNoTracking()
                .OrderBy(f => f.FormId)
                .Select(f => new { f.FormId, f.Name, f.RetentionDays })
                .ToListAsync()
                .ConfigureAwait(false))
            .Select(f => (f.FormId, f.Name, f.RetentionDays))
            .ToList());

    /// <summary>Where a site's forms are live: (subject kind, subject id, form id), sorted.</summary>
    public Task<List<(string Kind, Guid Subject, string FormId)>> PlacementsAsync(Guid? site = null) =>
        WithFormsAsync(async database => (await database.ForSite<QuickBuilder.Forms.Domain.FormPlacement>(site ?? Site)
                .AsNoTracking()
                .Select(p => new { p.SubjectKind, p.SubjectId, p.FormId })
                .ToListAsync()
                .ConfigureAwait(false))
            .Select(p => (p.SubjectKind, p.SubjectId, p.FormId))
            .OrderBy(p => p.FormId, StringComparer.Ordinal)
            .ThenBy(p => p.SubjectKind, StringComparer.Ordinal)
            .ToList());

    /// <summary>A visitor of site A (or another host).</summary>
    public HttpClient Visitor(string host = CmsScenario.HostA) => App.CreateClientForHost(host);

    /// <summary>
    /// Posts a form as a browser does: urlencoded, the page's hidden fields, then the values.
    /// With <paramref name="json"/>, as the form script does; <paramref name="from"/> sets the
    /// client address the server sees.
    /// </summary>
    public Task<HttpResponseMessage> SubmitAsync(
        string formId, Guid pageId, string path, IEnumerable<KeyValuePair<string, string>> values, bool json = false, string host = CmsScenario.HostA, string? from = null) =>
        SubmitAsync(App, formId, pageId, path, values, json, host, from is null ? null : r => r.Headers.Add(Infrastructure.IdentitySeed.RemoteAddressHeader, from));

    /// <summary>As above, through <paramref name="app"/> (another instance), with the request adjusted by <paramref name="adjust"/>.</summary>
    public static async Task<HttpResponseMessage> SubmitAsync(
        Infrastructure.QuickBuilderApp app,
        string formId,
        Guid pageId,
        string path,
        IEnumerable<KeyValuePair<string, string>> values,
        bool json = false,
        string host = CmsScenario.HostA,
        Action<HttpRequestMessage>? adjust = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var client = app.CreateClientForHost(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/_forms/{formId}", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
                [new("_page", pageId.ToString()), new("_path", path), .. values]),
        };
        if (json)
        {
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        adjust?.Invoke(request);

        var response = await client.SendAsync(request).ConfigureAwait(false);
        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
        return response;
    }

    /// <summary>A site's stored submissions, oldest first.</summary>
    public Task<List<QuickBuilder.Forms.Domain.Submission>> SubmissionsAsync(Guid? site = null) =>
        WithFormsAsync(database => database.ForSite<QuickBuilder.Forms.Domain.Submission>(site ?? Site)
            .AsNoTracking()
            .OrderBy(s => s.ReceivedAtUtc)
            .ToListAsync());

    /// <summary>A page document: one Section holding the given blocks, after any component references.</summary>
    public static string Page(IEnumerable<Guid> componentIds, params string[] blocks)
    {
        var refs = string.Concat(componentIds.Select((id, i) =>
            $$"""{"id":"ref{{i}}","type":"component-ref","props":{"componentId":"{{id}}"},"children":[]},"""));
        return """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":["""
            + refs
            + """{"id":"s1","type":"section","props":{},"children":["""
            + string.Join(",", blocks)
            + "]}]}}";
    }

    /// <summary>A page or component document: one Section holding the given blocks.</summary>
    public static string Document(params string[] blocks) => Page([], blocks);

    /// <summary>
    /// A complete form: an email field <c>e00000000001</c> (required) and a text field
    /// <c>f00000000001</c>, with the given extra props (JSON members, comma-led).
    /// </summary>
    public static string Form(string id, string name = "Contact", string extraProps = "") =>
        $$"""{"id":"{{id}}","type":"form","props":{"name":{{JsonSerializer.Serialize(name)}},"fields":[{"id":"e00000000001","type":"email","label":"Email","required":true},{"id":"f00000000001","type":"text","label":"Name"}]{{extraProps}}},"children":[]}""";

    private async Task PublishAsync(string subjectPath, string document)
    {
        await PublishingApi.SaveAsync(Admin, subjectPath, document).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(Admin, subjectPath).ConfigureAwait(false);
    }

    public void Dispose() => Scenario.Dispose();
}
